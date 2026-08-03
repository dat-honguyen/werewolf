# Design: home-lab deploy for werewolf (BE + FE), via GHCR, as a second/parallel path to AWS

**Goal:** stand up a second, independent deploy path for both `werewolf` (backend) and `werewolf-frontend`
onto a home Debian box ("werwolf", AMD 8600G, Podman + podman-compose already installed), fronted by a
new Cloudflare Tunnel, pushing images to GitHub Container Registry (GHCR) instead of ECR. This runs
**alongside** the existing AWS path (EC2/RDS/ECR/S3+CloudFront), not replacing it yet — AWS gets dropped
later, at which point disabling it should be as simple as turning off one workflow. The platform pieces
(tunnel, Quadlet convention, self-hosted runner) are deliberately generic so future projects reuse them
without re-deriving this design.

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

- **Directory convention — `/opt/homelab/<project>/` is the source of truth; `/etc/containers/
  systemd/` holds symlinks into it.** Podman/systemd requires the actual unit files to be discoverable
  under `/etc/containers/systemd/` (system-wide) — that requirement doesn't go away — but the *editable,
  version-controllable* copy lives at `/opt/homelab/<project>/*.container` (and `.network`/`.volume`
  units, plus that project's `.env`), and `/etc/containers/systemd/` holds a symlink per unit file
  pointing back into `/opt/homelab/<project>/`:
  ```
  ln -s /opt/homelab/werewolf/werewolf-api.container /etc/containers/systemd/werewolf-api.container
  ```
  This keeps every project's files in one predictable place (`/opt/homelab/<project>/`) for editing,
  backup, and future tooling, while still satisfying systemd's fixed unit-file search path — editing
  the file under `/opt/homelab/<project>/` and running `systemctl daemon-reload` is enough; no
  copy-then-forget-to-update-the-copy step. (Generating the unit fresh each deploy and copying it over
  is a viable alternative but reintroduces a "which one is current" question the symlink avoids.)

## 2. CI/CD path: self-hosted GitHub Actions runner on the box

Originally considered a pull-based systemd timer to dodge the box's sleep-for-power habit, but a
**self-hosted GitHub Actions runner queues the job and waits rather than failing** when the runner is
offline (default queue timeout is generous — hours, not minutes), so it tolerates sleep just as well
while being far less to build and maintain: no custom polling script, no `.timer` unit, no digest-diff
logic. Revisit only if queued-job wait time in practice turns out to be too short for how long the box
stays asleep.

- Install the GitHub Actions self-hosted runner directly on the Debian box (one-time setup, registered
  against the repo or an org, with a label like `home-werwolf` — reusable for future projects' workflows
  by targeting the same label).
- The deploy job in each workflow targets `runs-on: [self-hosted, home-werwolf]`. If the box is asleep,
  the job sits queued until the runner reconnects, then runs — no separate mechanism needed to "wait
  for awake."
- That job does the pull + restart directly: `podman pull ghcr.io/<owner>/<image>:<sha>` (the immutable
  tag from the triggering run — see "Immutable image tags" below, not `:latest`) + `systemctl restart
  <project>.service` (or `--user` equivalent for rootless Quadlet units) — plain steps in the workflow,
  not a script maintained separately on the box.
- Build (compiling/pushing the image to GHCR) can still happen on `ubuntu-latest` as normal — only the
  final "deploy to this box" job needs `runs-on: self-hosted`, keeping build environment consistent
  with the AWS workflow and avoiding tying up the home runner with compute it doesn't need to do.

### Immutable image tags (deploy `:<sha>`, not `:latest`)

Both `:latest` and `:<sha>` are still published to GHCR (`:latest` is convenient for manually poking
around the registry), but **the `deploy` job always pulls and runs the immutable `:<sha>` tag from the
run that triggered it**, never `:latest`. Reasons:

- **Race conditions**: if two releases publish before the self-hosted runner wakes up, `:latest` on
  the box at wake time is whichever image was pushed *last*, not necessarily the one whose `deploy` job
  is actually running — a queued job for release A could wake up and pull release B's `:latest`. Pulling
  the SHA baked into that specific run's own artifacts/env avoids this entirely: each `deploy` job pulls
  exactly the image its own `build` job produced, regardless of what's been pushed since.
- **Exact match**: the deployed image is guaranteed to be the one built from the commit that triggered
  the workflow, not "whatever the latest push happens to be by the time the job executes."
- **Rollback**: redeploying an older release is "pull that old SHA," no rebuild — see the Rollback
  section below.

Mechanically: the `build` job's `APP_VERSION`/tag computation already produces `${{ github.sha }}`; the
`deploy` job either receives it via `needs.build.outputs` (job output) or recomputes `${{ github.sha }}`
directly (same value, no extra plumbing needed since both jobs run in the same workflow trigger).

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

New `.github/workflows/deploy-homelab.yml`, two jobs:
1. **`build`** (`runs-on: ubuntu-latest`): checkout, compute `APP_VERSION` (same logic as `deploy.yml`
   today), `docker/login-action` against `ghcr.io`, build the existing `Dockerfile` unchanged, tag +
   push `:${{ github.sha }}` and `:latest`.
2. **`deploy`** (`runs-on: [self-hosted, home-werwolf]`, `needs: build`): `podman pull
   ghcr.io/<owner>/werewolf:${{ github.sha }}` + `systemctl restart werewolf-api.service` (or `--user`
   for rootless) + **post-deploy health check** (see below). Queues until the box is awake if it's
   asleep at release time.
3. Quadlet's `.container` unit for `werewolf-api` should reference the same `:${{ github.sha }}` tag
   (templated into the unit file, or the `deploy` step updates the unit's `Image=` line before
   restarting) so `systemctl restart` alone — without a fresh `podman pull`/unit edit — reproduces the
   exact running image, e.g. after an unrelated reboot.

**Post-deployment health check** (part of the `deploy` job, after the restart):
```
systemctl restart werewolf-api.service
sleep 5
curl --fail --retry 5 --retry-delay 3 http://localhost:5000/health
```
If the health check fails, the job fails — a bad image doesn't get silently left running. (Adjust the
path to whatever the backend's actual readiness endpoint is, e.g. `/health/ready` per the existing
`move-postgres-to-ec2-plan.md` smoke-test reference.)

On the box (one-time setup, reusable pattern for future projects):
- `postgres.container` and `werewolf-api.container` Quadlet units, both on `homelab.network`.
- DB connection string lives in `/opt/homelab/werewolf/.env`, referenced via Quadlet's
  `EnvironmentFile=` — manually maintained on the box, not fetched from AWS SSM (removes that AWS
  coupling now rather than as part of a later cleanup).
- **Postgres persistent storage**: a bind-mounted host path, not an anonymous/named Podman volume —
  e.g. `/opt/homelab/werewolf/pgdata:/var/lib/postgresql/data` in the `postgres.container` Quadlet unit
  (`Volume=/opt/homelab/werewolf/pgdata:/var/lib/postgresql/data:Z`). A named volume works too but its
  actual on-disk location (`podman volume inspect`) is less obvious to a future maintainer or backup
  script than a plain, documented path under the project's own directory — so the bind mount is
  preferred here specifically for discoverability: `/opt/homelab/werewolf/pgdata` is where backups
  point, no `podman volume inspect` required to find the data.
- **Pin the Postgres image to a specific major version**, `postgres:17` (matching the version already
  used in `docker-compose.yml` and on EC2) — not `postgres:latest`, which would silently jump major
  versions on a future `podman pull` and require a manual `pg_upgrade` the Quadlet restart won't do
  for you.

## 5. Frontend deploy shape (werewolf-frontend repo)

Today's S3+CloudFront `deploy.yml` is untouched. New `deploy-homelab.yml`:
1. `npm ci`, write version file, `npm run build -- --configuration production` (unchanged steps).
2. **New Dockerfile** (doesn't exist yet — FE is pure static S3 today): pin a specific nginx-alpine
   version tag (e.g. `nginx:1.27-alpine`, not the floating `nginx:alpine`, which can silently pick up a
   new minor/patch build on rebuild), `COPY dist/werewolf-frontend/browser` into
   `/usr/share/nginx/html`, plus a small `nginx.conf` with `try_files $uri /index.html` for Angular's
   client-side routing.
3. Build + push to `ghcr.io/<owner>/werewolf-frontend:<sha>` / `:latest`, same `build`/`deploy` two-job
   split as the backend — `deploy` job on `runs-on: [self-hosted, home-werwolf]` pulls the `:${{
   github.sha }}` tag (never `:latest`, same reasoning as the backend) and restarts.

**Post-deployment verification** for the frontend: instead of an app-specific health path, confirm
nginx itself is serving successfully:
```
systemctl restart werewolf-frontend.service
sleep 3
curl --fail --retry 5 --retry-delay 3 http://localhost:8081/
```

On the box: a `werewolf-frontend.container` Quadlet unit, its own tunnel ingress hostname.

## 6. Rollback

Because every deploy runs an immutable `:<sha>` tag rather than `:latest` (section 2), rolling back is
fast and deterministic — no rebuild, no republish, just point at an image already sitting in GHCR:

1. Pick the previous known-good SHA (from GitHub's release history or `git log`).
2. On the box: `podman pull ghcr.io/<owner>/werewolf:<previous-sha>`.
3. Update the Quadlet unit's `Image=` line to that SHA (the same `/opt/homelab/werewolf/werewolf-
   api.container` file from section 1) and `systemctl daemon-reload`.
4. `systemctl restart werewolf-api.service`, then the same health check from section 4 to confirm it
   came back up clean.

This can be done manually over SSH, or as a `workflow_dispatch` input (`sha` to redeploy) on
`deploy-homelab.yml` reusing the existing `deploy` job — either way, no source rebuild is involved,
which is the whole point of publishing SHA-tagged images in the first place.

## 7. Onboarding a future project onto this platform

The pieces above (tunnel, runner, Quadlet convention) are per-box, one-time setup. Once they exist,
adding a *new* project follows the same short checklist every time:

1. **Build & publish a GHCR image** for the new project — a `build` job on `ubuntu-latest` pushing
   `:<sha>` and `:latest`, same shape as sections 4/5.
2. **Write Quadlet unit(s)** under `/opt/homelab/<new-project>/*.container` (app, and `.network`/
   `.volume` if it needs its own DB), symlinked into `/etc/containers/systemd/` per section 1.
3. **Configure `.env`** at `/opt/homelab/<new-project>/.env` with whatever secrets/connection strings
   that project needs, referenced via the unit's `EnvironmentFile=`.
4. **Add a Cloudflare Tunnel ingress line** for the new hostname, pointing at the project's local port,
   in the shared `cloudflared` `config.yml` (section 1) — `systemctl restart cloudflared` to pick it up.
5. **Add the DNS record** (CNAME to the tunnel) for that hostname in Cloudflare.
6. **Copy the reusable `deploy-homelab.yml` workflow** into the new project's repo, swapping image/
   service names — same `build` + `deploy` (`runs-on: [self-hosted, home-werwolf]`) shape, same
   SHA-tag-not-latest rule, same post-deploy health check pattern.
7. **Enable and start the service**: `systemctl daemon-reload && systemctl enable --now <project>.service`.

This is the checklist that makes the platform reusable rather than werewolf-specific — steps 1-7 are
the same regardless of which project is being onboarded.

## Outcome

Both repos end up with an `aws` workflow and a `homelab` workflow side by side, fully independent.
Flipping to home-lab-only later is "rename `deploy.yml` to `deploy.yml.bak` (or disable the trigger)" —
no rewrite of the home-lab path needed, and the Quadlet + tunnel-ingress + self-hosted-runner pattern
established here is what future projects copy, not something werewolf-specific.
