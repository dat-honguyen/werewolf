# Plan: move Postgres off RDS onto the existing EC2 box (with a future Pi 4 option)

**Goal:** drop the RDS instance (~$0.7-0.9/day of the current ~$1.3-1.5/day real cost) by running
Postgres as a container on the same EC2 box already running the API, without breaking the existing
`deploy.yml` GitHub Action or losing an easy rollback path. Starting fresh on the new database (no
data migration from RDS, no ongoing backup job) — acceptable for an alpha-stage hobby project where
existing room/game data is disposable. A second section below sketches the later, optional move
from that EC2 box to a Raspberry Pi 4.

**Current setup (context for why each step below is shaped the way it is):**
- `.github/workflows/deploy.yml` triggers on `release: published` (or manual dispatch): builds the
  Docker image, pushes to ECR, then uses **SSM `send-command`** (not SSH) to run a shell script on
  the EC2 instance that pulls the new image, fetches the DB connection string fresh from SSM
  Parameter Store (`/werewolf/prod/db-connection-string`, SecureString), and does
  `docker stop/rm/run` on a container named `werewolf-api`.
- The app container currently runs on the default Docker bridge network (`-p 5000:8080`, no
  `--network` flag) — it doesn't share a network with anything else on the box today.
- `docker-compose.yml` at the repo root already defines a `postgres:17` service — currently used
  for local dev only. This is reusable as-is for the EC2 side.

---

## Phase 1 — Stand up Postgres on EC2, RDS untouched (fully reversible)

1. **New workflow, not a change to the existing one**: add
   `.github/workflows/deploy-db.yml`, `workflow_dispatch`-only (never auto-triggers), mirroring
   `deploy.yml`'s SSM-command pattern. It should be idempotent — safe to re-run — and do roughly:
   ```
   docker network create werewolf-net || true
   DB_CONN=$(aws ssm get-parameter --name /werewolf/prod/db-connection-string --with-decryption ...)
   DB_USER=$(parse Username= out of $DB_CONN)
   DB_PASSWORD=$(parse Password= out of $DB_CONN)
   DB_NAME=$(parse Database= out of $DB_CONN)
   docker run -d --name werewolf-postgres --restart unless-stopped \
     --network werewolf-net \
     -p 127.0.0.1:5432:5432 \
     -e POSTGRES_DB="$DB_NAME" -e POSTGRES_USER="$DB_USER" -e POSTGRES_PASSWORD="$DB_PASSWORD" \
     -v werewolf-pgdata:/var/lib/postgresql/data \
     postgres:17
   ```
   - Joined to a dedicated `werewolf-net` network so the app container can reach it by container
     name (`werewolf-postgres`) once it's on the same network (Phase 3).
   - `-p 127.0.0.1:5432:5432` binds the port to the EC2 host's **loopback only** — not
     `0.0.0.0`, so it's never reachable from the internet, but it *is* reachable from an SSM
     port-forwarding session (see "Accessing the DB directly" below), the same way you'd tunnel
     into RDS today.
   - Username/password/database are reused straight from the **existing** `db-connection-string`
     parameter (the current RDS credentials) rather than minting a new SSM parameter — this keeps
     Phase 2's cutover to a pure `Host=` swap in that same parameter, no credential rotation
     involved.
2. Manually run this new workflow once. `deploy.yml` is completely untouched at this point — the
   app is still pointed at RDS, nothing about the live site changes yet.
3. Verify via SSM (`aws ssm send-command` with `AWS-RunShellScript`, same mechanism the deploy
   workflow already uses) that `docker exec werewolf-postgres pg_isready` succeeds.

### Accessing the DB directly (like you could with RDS)

No bastion host, no public exposure — tunnel in via SSM Session Manager (needs the
`session-manager-plugin` installed locally for the AWS CLI):

```
aws ssm start-session --target <instance-id> \
  --document-name AWS-StartPortForwardingSession \
  --parameters '{"portNumber":["5432"],"localPortNumber":["5432"]}'
```

Then point `psql`/DBeaver/pgAdmin/whatever at `localhost:5432` from your own machine, same as an
RDS tunnel. This works because of the `127.0.0.1:5432:5432` binding in step 1 — the tunnel lands on
the EC2 host's loopback, which is exactly what that binding exposes.

## Phase 2 — Cut the app over to the local Postgres

5. Update the **existing** SSM parameter `/werewolf/prod/db-connection-string` in place — same
   name, same mechanism `deploy.yml` already reads from — to point at the container instead of
   RDS: `Host=werewolf-postgres;Port=5432;Database=werewolf;Username=werewolf;Password=...`.
   Because `deploy.yml` re-fetches this value fresh on every deploy, **this one parameter change is
   the only thing that determines which database the app talks to** — no workflow YAML needs to
   change for the connection string itself.
6. One small, necessary edit to `deploy.yml`: the app's `docker run` needs
   `--network werewolf-net` added so it can resolve `werewolf-postgres` by container name (right
   now it's on the default bridge network, which can't see that name). This is the only line that
   touches the existing workflow.
7. Trigger `deploy.yml` (as normal, via a release or manual dispatch). This restarts
   `werewolf-api` on the new network with the new connection string, against a fresh, empty
   database — Marten will create its schema/tables on first use, same as it did against RDS
   originally.
8. Smoke-test: `curl https://api-werewolf.datisa.dev/health/ready`, then a real lobby
   create/join through the actual frontend.

**Rollback at any point up to here:** flip the SSM parameter back to the RDS connection string and
re-run `deploy.yml` — RDS hasn't been touched, so this is a one-parameter, one-redeploy revert.

## Phase 3 — Only once you're confident (this is where the money is actually saved)

9. `aws rds delete-db-instance` (with `--skip-final-snapshot` since we're not preserving data) once
   you've run on the new setup for a few days without issues.
10. Clean up the now-unused RDS security group / subnet group / parameter group if nothing else
    references them.

---

# Future, optional: moving from EC2 to a Raspberry Pi 4

Not urgent given Phase 3 above already gets you to roughly $0.86/day on AWS with no self-hosting
risk — but if you still want to get off AWS entirely later, here's the shape of it:

- **Images already portable**: `postgres:17` and `mcr.microsoft.com/dotnet/aspnet:10.0` both ship
  official `arm64` variants under the same tags, so the existing `Dockerfile`/`docker-compose.yml`
  should build and run on a Pi's ARM64 Linux with no changes.
- **Networking** (the real difference from EC2): a Pi at home has no public IP by default.
  Recommend a **Cloudflare Tunnel** over port-forwarding + Dynamic DNS — no exposed home IP, no
  router config, and it handles TLS for you. Point `api-werewolf.datisa.dev` at the tunnel instead
  of an EC2 Elastic IP.
- **CI/CD**: SSM won't reach a home Pi. Simplest replacement: install a **self-hosted GitHub Actions
  runner** directly on the Pi, and change `deploy.yml`'s `runs-on: ubuntu-latest` to
  `runs-on: self-hosted` for that job — the build-and-run steps stay conceptually the same, just
  executed locally on the Pi instead of round-tripping through ECR + SSM.
- **DB access**: same idea as the SSM tunnel above, but simpler — a Cloudflare Tunnel or plain SSH
  port-forward straight to the Pi's loopback-bound Postgres port, no AWS involved.
- **Reliability tradeoff to accept going in**: no AWS-grade redundancy — a power blip or home
  internet drop takes the game down until it's back. A cheap UPS for the Pi mitigates the power
  half of that; there's no good mitigation for an ISP outage short of a cellular failover, which is
  probably overkill for a hobby project.
