# Design: home-lab deploy for werewolf (BE + FE), via GHCR, as a second/parallel path to AWS

**Goal:** stand up a second, independent deploy path for both `werewolf` (backend) and `werewolf-frontend`
onto a home Debian box ("werwolf", AMD 8600G, Podman + podman-compose already installed), fronted by a
new Cloudflare Tunnel, pushing images to GitHub Container Registry (GHCR) instead of ECR. This runs
**alongside** the existing AWS path (EC2/RDS/ECR/S3+CloudFront), not replacing it yet — AWS gets dropped
later, at which point disabling it should be as simple as turning off one workflow. The platform pieces
(tunnel, Quadlet convention, pull-based deploy script) are deliberately generic so future projects reuse
them without re-deriving this design.

**Non-goals for this pass:** migrating existing AWS data, removing any AWS resource, WOL/always-on
tooling, a shared reverse proxy (Cloudflare Tunnel's own ingress routing replaces that need).

## Context: current AWS setup (for contrast)

- `werewolf/.github/workflows/deploy.yml`: builds Docker image, pushes to ECR, deploys to EC2 via
  SSM `send-command` (`docker stop/rm/run` for a container named `werewolf-api`), DB connection string
  fetched fresh from AWS SSM Parameter Store on every deploy.
- `werewolf/.github/workflows/deploy-db.yml`: stands up Postgres as a container on the same EC2 box
  (see `docs/move-postgres-to-ec2-plan.md`, already executed).
- `werewolf-frontend/.github/workflows/deploy.yml`: builds Angular, syncs to S3, purges Cloudflare cache.
- Both repos' `deploy.yml` trigger on `release: published` or `workflow_dispatch`.

None of the above changes in this design — a new, parallel `deploy-homelab.yml` is added to each repo.

## 1. Reusable home-lab platform layer

- **One Cloudflare Tunnel per box**, not per project. Single `cloudflared` `config.yml` with an
  `ingress:` list mapping hostnames to `localhost` ports, e.g.:
  ```yaml
  ingress:
    - hostname: api-werewolf.home.example.com
      service: http://localhost:5000
    - hostname: werewolf.home.example.com
      service: http://localhost:8081
    - service: http_status:404
  ```
  Adding a future project = one new ingress line + a DNS CNAME to the tunnel + `systemctl restart
  cloudflared`. Cloudflare's edge terminates TLS; the tunnel forwards plaintext to `localhost:<port>`.
  New Cloudflare Tunnel setup needed (no existing home-lab tunnel to extend).

- **Podman Quadlet** instead of raw `podman run`/`podman-compose up`: `.container`/`.network` unit
  files under `/etc/containers/systemd/` (system-wide) or `~/.config/containers/systemd/` (rootless),
  auto-generating systemd services at boot. Chosen over podman-compose (already installed, but no
  boot-time/restart-on-crash story without extra tooling) and over Portainer (a GUI/management layer,
  not a deploy mechanism — doesn't replace needing something to define restart/boot behavior; adds an
  always-on container on a box that's meant to sleep). A shared `homelab.network` Quadlet network lets
  a project's containers resolve each other by name, playing the same role `werewolf-net` plays on EC2.

- **Directory convention**: `/opt/homelab/<project>/` holds that project's Quadlet files and `.env`.
  Same layout for every future project.

## 2. CI/CD path: pull-based, via systemd timer

The box is put to sleep to save power, which rules out anything that needs the box reachable *at the
moment* a release fires (self-hosted GitHub Actions runner sitting idle — dies when asleep, no retry
for an offline runner; SSH-push-on-release — fails silently if asleep; SSH-push + Wake-on-LAN — needs a
second always-on LAN device to send the magic packet, extra moving part for no real benefit).

Chosen approach: **the box pulls on its own schedule**, independent of when CI runs:

- A generic script (parameterized by project/image/service name, reused across projects) run by a
  `.timer` unit every 15–30 min: check the registry for a new image digest, and if changed,
  `podman pull` + `systemctl restart <project>.service`.
- If the box is asleep, the timer just doesn't fire until next awake, then catches up on the next tick.
- The same script is kept available for manual on-demand use (SSH in, run it directly) when immediate
  deploy is wanted and the box is known to be awake.
- GitHub Actions' job responsibility ends at "build and push the image" — it never tries to reach the
  box directly. This is what decouples release timing from box uptime.

## 3. Registry: GHCR, as a second/parallel path (not a replacement, yet)

Switching now (rather than waiting for the AWS-drop) because GHCR is free, needs zero new secrets
(`GITHUB_TOKEN` already available in Actions, vs. AWS access keys), and removes one AWS dependency
early as an independent, low-risk change:

- `docker/login-action` against `ghcr.io` using `${{ secrets.GITHUB_TOKEN }}`.
- Images: `ghcr.io/<owner>/werewolf:<sha>` / `:latest`, `ghcr.io/<owner>/werewolf-frontend:<sha>` /
  `:latest`.
- The box needs a GitHub PAT (classic, `read:packages` scope, or fine-grained equivalent) for `podman
  login ghcr.io`, stored in the box's local `.env`/systemd credential — replaces AWS access keys with
  one long-lived token, no AWS SDK/CLI needed on the box at all.
- Ruled out: Docker Hub (anonymous/authenticated pull-rate limits a polling timer could hit over time).

**Deliberately kept fully independent of `deploy.yml`/`deploy-db.yml`**: new workflow file
(`deploy-homelab.yml`), same triggers (`release: published` + `workflow_dispatch`), separate job, no
shared steps or secrets with the AWS workflow. When AWS is eventually dropped, disabling it is
"disable/delete `deploy.yml`" with zero risk to the home-lab path — and per the user's ask, the AWS
config should be preserved as a `.bak` (e.g. `deploy.yml.bak`) rather than deleted outright, in case
of rollback.

## 4. Backend deploy shape (werewolf repo)

New `.github/workflows/deploy-homelab.yml`:
1. Checkout, compute `APP_VERSION` (same logic as `deploy.yml` today).
2. `docker/login-action` against `ghcr.io`.
3. Build the existing `Dockerfile` unchanged, tag + push `:${{ github.sha }}` and `:latest`.
4. Workflow ends there — no SSH/SSM step. The box's own timer (section 2) picks up the new digest.

On the box (one-time setup, reusable pattern for future projects):
- `postgres.container` and `werewolf-api.container` Quadlet units, both on `homelab.network`.
- DB connection string lives in `/opt/homelab/werewolf/.env`, referenced via Quadlet's
  `EnvironmentFile=` — manually maintained on the box, not fetched from AWS SSM (removes that AWS
  coupling now rather than as part of a later cleanup).

## 5. Frontend deploy shape (werewolf-frontend repo)

Today's S3+CloudFront `deploy.yml` is untouched. New `deploy-homelab.yml`:
1. `npm ci`, write version file, `npm run build -- --configuration production` (unchanged steps).
2. **New Dockerfile** (doesn't exist yet — FE is pure static S3 today): `nginx:alpine`, `COPY dist/
   werewolf-frontend/browser` into `/usr/share/nginx/html`, plus a small `nginx.conf` with
   `try_files $uri /index.html` for Angular's client-side routing.
3. Build + push to `ghcr.io/<owner>/werewolf-frontend:latest`, same as backend — no direct box contact
   from CI.

On the box: a `werewolf-frontend.container` Quadlet unit, its own tunnel ingress hostname.

## Outcome

Both repos end up with an `aws` workflow and a `homelab` workflow side by side, fully independent.
Flipping to home-lab-only later is "rename `deploy.yml` to `deploy.yml.bak` (or disable the trigger)" —
no rewrite of the home-lab path needed, and the Quadlet + tunnel-ingress + pull-timer pattern
established here is what future projects copy, not something werewolf-specific.
