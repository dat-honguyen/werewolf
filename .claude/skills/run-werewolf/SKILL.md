---
name: run-werewolf
description: Build, run, and drive the Werewolf backend (Wolverine.Http + Marten API). Use when asked to start/run/build werewolf, hit its HTTP endpoints, verify a game-flow change end-to-end, or check its health.
---

Werewolf is an ASP.NET Core + WolverineFx.Http + Marten (event-sourced, Postgres) backend with no UI — drive it with `curl` against its JSON endpoints, backed by a Postgres container. Use `.claude/skills/run-werewolf/driver.sh` (all paths below are relative to the repo root, `/home/dat98/s/lion/werewolf`).

## Prerequisites

.NET SDK 10 is already installed at `/home/dat98/.dotnet/dotnet`. Postgres runs via `docker-compose.yml` at the repo root — no local Postgres install needed.

**Important:** on this box `docker` is a **zsh alias** for `podman` (`alias docker=podman`), not a real binary. It works when you type `docker ...` interactively, but is invisible to non-interactive scripts (`docker: command not found`). The driver calls `podman compose` / `podman` directly for this reason — don't "fix" it back to `docker` if you see it in the script.

## Build

```bash
/home/dat98/.dotnet/dotnet build src/Application/Application.csproj
```

`src/Application/Application.csproj` references `WolverineFx.RuntimeCompilation`, but **only in `Debug` config** — required for Wolverine's dynamic (dev-time) code generation. A `Release` build intentionally omits it and stays Roslyn-free; don't add it there.

## Run (agent path)

Use the driver — it handles the Postgres container, the build, and the background launch with the right env var:

```bash
.claude/skills/run-werewolf/driver.sh up      # start Postgres, build, launch app, wait for /health/ready
.claude/skills/run-werewolf/driver.sh smoke   # create a lobby, join a second player, verify via Postgres
.claude/skills/run-werewolf/driver.sh status  # check Postgres container + app health
.claude/skills/run-werewolf/driver.sh down    # stop the app (Postgres is left running)
```

`up` prints the base URL (`http://localhost:5080`) and where the app's stdout log lands (`.claude/skills/run-werewolf/.app.log`) — tail that for Wolverine/Marten SQL and request logs.

Example manual request once `up` has run:

```bash
curl -sS -X POST http://localhost:5080/api/v1/lobby \
  -H "Content-Type: application/json" \
  -d '{"hostPlayerId":"11111111-1111-1111-1111-111111111111","hostDisplayName":"Alice"}'
# => {"roomCode":"AB12CD"}
```

All routes are `POST /api/v1/lobby/...` and `POST /api/v1/game/...` (grep `WolverinePost` under `src/Application/Werewolf/` for the full list — there are no `GET` read-model endpoints yet, only commands).

| driver command | what it does |
|---|---|
| `up` | `podman compose up -d` (idempotent — handles "container already exists"), `dotnet build`, kills a stale listener on the port if one is stuck, launches `dotnet run` in the background, polls `/health/ready` |
| `smoke` | POSTs a lobby create + join, asserts against the event stream and the `RoomLobbyView` read model (see Gotchas — do **not** check `mt_doc_lobbystate`) |
| `status` | one-shot health check, no side effects |
| `down` | kills whatever is actually listening on the port (see Gotchas) |

## Run (human path)

Open the repo in Rider (a `Werewolf.slnx` solution and `src/Application/Properties/launchSettings.json` are already set up — profile `https`, binds `https://localhost:5443` and `http://localhost:5080`) and hit the green Run arrow. Postgres still needs to be up first: `podman compose up -d` from the repo root.

## Gotchas

- **`docker` is a shell alias, invisible to scripts.** `type docker` → `docker is an alias for podman`. Any non-interactive script (including this driver) must call `podman`/`podman compose` directly.
- **The listening process is named `Application`, not `dotnet`.** `dotnet run` execs into an apphost binary at `bin/Debug/net10.0/Application`, and *that* is what binds the port — `pgrep -f Application.dll` or `pgrep -f "dotnet run"` will **not** find it, and killing the `dotnet run` wrapper's PID leaves the real listener running, so a later `up` fails with "address already in use." Find the real PID with `ss -ltnp | grep :5080` and kill that one (the driver's `down` does this correctly).
- **`LobbyState`/`GameState` are `LiveStreamAggregation`, not a persisted `Snapshot` — this is intentional, don't "fix" it.** `mt_doc_lobbystate` / `mt_doc_gamestate` will *always* be empty; that's how Live aggregation works (computed on demand from raw events, never written to a document table). It's tempting to conclude commands are silently no-op'ing when that table stays empty after a `POST` — they aren't. Verify against the event stream (`mt_events` joined through `mt_natural_key_lobbystate`) or the actual read models (`mt_doc_roomlobbyview`, `mt_doc_playergameview`) instead.
- **"Warning: The async daemon is disabled" in the startup log is a red herring for the read models.** `RoomLobbyViewProjection`/`PlayerGameViewProjection` are registered `Async`, and that warning makes it look like they'll never run — but `CritterConfiguration.cs` sets `UseWolverineManagedEventSubscriptionDistribution = true`, which drives these via Wolverine's own subscription distribution instead of Marten's daemon. They do catch up, just not synchronously with the triggering request — poll for up to a few seconds in tests instead of asserting immediately (the driver's `smoke` command does this).
- **`ASPNETCORE_ENVIRONMENT` must be `Development`, or you'll hit two different failures depending on what else is missing:** unset (defaults to Production) → `CritterStackDefaults` requires pre-generated Wolverine code (`ExpectedTypeMissingException`); set to `Development` without the `WolverineFx.RuntimeCompilation` package → `InvalidOperationException: No IAssemblyGenerator is registered`. The driver always sets `ASPNETCORE_ENVIRONMENT=Development` and the csproj has the package Debug-only, so this shouldn't recur — but if you build/run outside the driver, remember both halves.
- **CLI diagnostics (`dotnet run -- codegen preview`, `wolverine-diagnostics codegen-preview`) need `Program.cs` to end with `return await app.RunJasperFxCommands(args);`** — it does now, but if that line is ever removed, any `dotnet run -- <verb>` invocation silently just boots the normal web server on port 5000 instead (ignoring the verb args) rather than erroring, which is confusing to debug.
- **`codegen-preview --route` prints an empty generated-code body even for routes that work correctly at runtime.** It's a limitation of the preview tool in this Dynamic-codegen setup, not a signal that the route is broken — don't use it to diagnose the LiveStreamAggregation gotcha above, use the SQL log or a real HTTP request instead.

## Troubleshooting

- **`docker: command not found` when a script runs `docker compose ...`**: see the alias gotcha above — use `podman compose` (or `COMPOSE=podman` env override the driver reads).
- **`address already in use` on `dotnet run --urls http://localhost:5080`**: a previous `Application` process is still bound to the port. `ss -ltnp | grep :5080` to find the real PID and kill it (see gotcha above); don't just kill the last `dotnet run`'s PID.
- **`System.ArgumentNullException: ... (Parameter 'Host')` from Npgsql at startup**: the resolved connection string is empty — check `appsettings.json`'s `ConnectionStrings:database` key matches what `CritterConfiguration.cs`/`WerewolfModule` code actually reads (`configuration.GetConnectionString("database")`).
- **`JasperFx.Events.Projections.InvalidProjectionException: Id type mismatch ... stream identity type is string ... but the aggregate document ... id type is Guid`**: `StoreOptions.Events.StreamIdentity` is set to `AsString` somewhere while the app's own code uses `Guid` stream ids (`Guid.NewGuid()` + `MartenOps.StartStream<T>(guid, ...)`). Don't set `StreamIdentity = StreamIdentity.AsString` in `CritterConfiguration.cs` — this app is Guid-keyed throughout.
- **`Unhandled exception ... No IAssemblyGenerator is registered in the application's service provider`**: `WolverineFx.RuntimeCompilation` isn't referenced (or you're running a `Release` build, where it's intentionally absent) while `TypeLoadMode.Dynamic` is active. Build/run `Debug` config for local dev.
