# claude.md

Werewolf — a Marten/Wolverine event-sourced social-deduction game backend, built with Wolverine.Http's
declarative aggregate-loading attributes and Wolverine.SignalR for real-time push.

## Environment

- `dotnet` executable path: `/home/dat98/.dotnet/dotnet`
- Bring the stack (Postgres + app) up/down: `.claude/skills/run-werewolf/driver.sh {up,down,status}`.
  `up` rebuilds, so run `down` then `up` to pick up code changes; `up` alone won't rebuild if already
  running.
- Regenerate Wolverine's ahead-of-time handler code after changing any handler chain (new event
  handler, new command, etc.): `dotnet run -- codegen write` from `src/Application/`.

## Reference project

`/home/dat98/s/lion/I-Learn-Mircorservice-and-Event-Sourcing` — another microservice/event-sourcing
sample (Marten-based, Application layout) to use as an additional example when comparing architecture
patterns.

## Layout

Everything lives under `src/Application/Werewolf/`:

- `Domain/` — shared primitives: `Role`, `GamePhase`, `WinningFaction` enums, `GameSettings`,
  `RoomCode` (custom JSON converter so it serializes as a plain string).
- `Lobby/` — one file per command (`CreateLobby.cs`, `JoinLobby.cs`, `StartGame.cs`, ...), each
  pairing the command record + its `*Endpoint` static class in the same file.
- `Game/` — one folder per night/day command (`SubmitWerewolfVote/`, `SubmitDoctorProtection/`,
  `CastVote/`, ...), plus the shared aggregate (`GameState.cs`), event definitions
  (`GameEvents.cs`), phase-completion logic (`NightChecklist.cs`, `GameCommandSupport.cs`,
  `DeathResolver.cs`, `WinConditionEvaluator.cs`), and the async cascade trigger
  (`GameFlowTriggerProjection.cs`).
- `ReadModels/` — Marten projections for `GET` reads and cross-cutting lookups.
- `Notifications/` — SignalR hub wiring (`JoinGameRoom.cs`) and the event-to-push translation layer
  (`PlayerNotification.cs`).
- `GetRoles/`, `GetRules/` — static reference endpoints describing the ruleset as implemented.

## Endpoint pattern (Wolverine.Http)

Example: `Game/SubmitWerewolfVote/SubmitWerewolfVoteEndpoint.cs`

- `Validate(command, [ReadAggregate("RoomCode")] GameState state, ...)` returns a `ProblemDetails`
  (always set `Status = StatusCodes.Status400BadRequest` explicitly — ASP.NET defaults an unset
  `Status` to 500) or `WolverineContinue.NoProblems`.
- `[WolverinePost("/api/v1/...")] Handle(command, [WriteAggregate("RoomCode")] GameState state)`
  returns an `Events` collection to append to the aggregate's stream. The attribute's string argument
  names the command property Wolverine uses to look up the aggregate (matched against a
  `[NaturalKey]`/`[NaturalKeySource]`-marked property on the aggregate, e.g. `GameState.RoomCode`).
- Never call `session.Events.FetchLatest<...>` (or `FetchForWriting`) by hand in an endpoint just to
  check existence — that bypasses Wolverine's built-in 404 handling and any manual
  `?? throw new InvalidOperationException(...)` on a missing aggregate surfaces as an unhandled 500,
  not a 404 (see `GetGameStateEndpoint`/`GetGameLogEndpoint`, which had this bug). Always resolve the
  aggregate via `[ReadAggregate("...")]`/`[WriteAggregate("...")]` instead — for GET endpoints keyed
  by route data (e.g. `roomCode`), bind an `[AsParameters]` record with a `[FromRoute]` property of
  the aggregate's natural-key type (`RoomCode` implements `IParsable<RoomCode>` for this) so
  `[ReadAggregate("PropName")]` can resolve the aggregate straight from the route, with no manual
  fetch or null-check.

## Aggregate example

`Game/GameState.cs` — a `LiveStreamAggregation<GameState>`-style write-side aggregate (no document
persistence of its own; loaded via `FetchLatest`/`[ReadAggregate]`/`[WriteAggregate]`). One
`Apply(SomeEvent)` method per event. Sub-state that must reset every night lives on
`NightActionsState` (recreated in `Apply(NightStarted)`); anything that must survive across nights
(e.g. `LastDoctorProtectedTarget`) lives directly on `GameState`.

## Projection examples

- Single-stream, `Inline` (immediate consistency for `GET` reads): `ReadModels/GameLogView.cs` —
  `SingleStreamProjection<GameLogView, Guid>`, one `Apply(IEvent<T>, GameLogView)` per event type,
  builds a human-readable play-by-play.
- Multi-stream: `ReadModels/PlayerDirectory.cs` — `MultiStreamProjection<PlayerDirectoryEntry, Guid>`,
  keys off different event types from different streams (`LobbyCreated`, `PlayerJoinedLobby`) into one
  document per player id.
- `Async` (not immediately consistent, used where the projection also triggers a side effect):
  `Game/GameFlowTriggerProjection.cs` watches the Game stream and publishes an internal
  `TryResolveNight`/`TryCloseVoting` message once a completeness predicate is met.

## SignalR notification pattern

`Notifications/PlayerNotification.cs` — `GameEventToNotificationHandler` has one `Handle(SomeEvent,
[ReadAggregate("GameId")] GameState state)` per event that should push to clients, returning
`SignalRMessage<PlayerNotification>` (single recipient/group) or `IEnumerable<object>` (fan-out to
several groups, e.g. every living werewolf). Two group scopes: `PlayerNotification.RoomGroup` (everyone
in the room) and `PlayerNotification.PlayerGroup` (one player only, joined via `JoinGameRoom` with a
`playerId`). Only events explicitly registered in
`Infrastructure/CritterConfiguration.cs`'s `PublishEventsToWolverine(... x.PublishEvent<T>())` list
reach these handlers — an event not in that list is folded into `GameState` but never pushed live.

## Single-tenant only

Configured in `src/Application/Infrastructure/CritterConfiguration.cs`:
- No multi-tenant document policy
- No conjoined tenancy setting
- Uses default Marten session (single tenant)

## Docs & scripts

- `GAME_FLOW.md` (repo root) — the FE integration reference: state machines, full HTTP API, call
  order, screen/button layouts, and the SignalR notification catalog. Keep it in sync whenever a rule,
  endpoint, or notification shape changes.
- `scripts/manual_playthrough.md` — copy-pasteable `curl` walkthrough for driving a full game by hand.
- `scripts/play_full_game.py` — scripted 8-player playthrough (deterministic bots) used to verify
  end-to-end behavior after rule changes; requires matplotlib for its timeline plot.
