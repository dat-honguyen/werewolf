# Werewolf

An event-sourced backend for a Werewolf (Mafia-style social deduction) party game, built on **WolverineFx** (command handling / messaging / HTTP) and **MartenDB** (Postgres-backed event store and projections).

Two independent aggregates, each keyed by a human-shareable 6-character room code:

- **Lobby** — room creation, joining, host controls, role/settings configuration, readying up.
- **Game** — role assignment, night actions, day voting, death resolution, win conditions.

A `StartGame` command bridges the two: it closes the Lobby stream and atomically starts a brand-new Game stream.

See [`make-a-plan-werewolf-calm-hippo.md`](./make-a-plan-werewolf-calm-hippo.md) for the original design doc this implementation follows.

## Project layout

Vertical-slice per command — each command, its handler, and (where one exists) its HTTP endpoint live together in their own folder:

```
src/Application/Werewolf/
  Domain/              Role, RoomCode, GamePhase, GameSettings — shared value types
  Lobby/
    LobbyState.cs       Lobby aggregate (folded state)
    LobbyEvents.cs       Lobby event types
    LobbyCommandSupport.cs   shared guards (EnsureOpen/EnsureHost/AssignRoles/...)
    CreateLobby/, JoinLobby/, LeaveLobby/, KickPlayer/, SetReady/,
    UpdateRoleDistribution/, UpdateGameSettings/, CancelLobby/, StartGame/
  Game/
    GameState.cs         Game aggregate (folded state)
    GameEvents.cs         Game event types
    GameCommandSupport.cs, NightChecklist.cs, DeathResolver.cs, WinConditionEvaluator.cs
    SubmitCupidPairing/, SubmitWerewolfVote/, SubmitDoctorProtection/, SubmitSeerInspection/,
    UseWitchHealPotion/, UseWitchPoisonPotion/, PassWitch/,
    SubmitHunterRevengeShot/, PassHunterRevenge/,
    AdvanceToVoting/, CastVote/, CloseVoting/
  ReadModels/           RoomLobbyView, PlayerGameView (role-scoped), GameLogView
  Notifications/        PlayerNotification -> SignalR push
```

Command handlers use Wolverine's declarative aggregate-handler workflow — `[WriteAggregate]` loads the
aggregate (by room code, via Marten Natural Keys) and appends the events a handler returns; there's no
manual `FetchForWriting`/`SaveChangesAsync` in the individual handlers. The exception is `StartGame`,
which bridges two streams (append to the existing Lobby stream, start a brand-new Game stream) in one
transaction — a combination `[WriteAggregate]` doesn't support, so it stays a manual
`IDocumentSession` handler.

## Game flow

### 1. Lobby

```
Open --JoinLobby/LeaveLobby/SetReady/UpdateRoleDistribution/UpdateGameSettings/KickPlayer--> Open
Open --StartGame (host, >=min players, roles valid, all ready or force-start)--> Closed  (bridges to a new Game)
Open --CancelLobby (host)--> Cancelled
```

The host is whoever created the lobby, or whoever inherits it via `HostTransferred` if the host leaves.

### 2. Game phases

```
RoleAssignment --(bridge from StartGame)--> Night
Night --all night roles resolved--> [Hunter revenge, if any] --> DayDiscussion
DayDiscussion --AdvanceToVoting (host)--> DayVoting
DayVoting --all alive voted, or CloseVoting (host)--> DayResolution
DayResolution --[Hunter revenge, if any]--> Night (next) | GameOver
```

`GameOver` is terminal — every command is rejected once reached.

### 3. Night sub-phase sequencing

Night is **one** event-sourced phase, not a linear state machine. Each role's action handler appends
its event, then re-checks `NightChecklist.IsComplete` — once every living role slot has acted (or has
no living holder, or has already exhausted its ability), the same handler call appends `NightResolved`
and the death cascade, in the same transaction. No timers or background jobs.

- **Cupid** (night 1 only): pairs two players as lovers — mandatory if alive.
- **Werewolves**: vote on a target; locks once all living wolves have voted — unanimously if
  `WerewolfRequiresConsensus`, otherwise by plurality.
- **Doctor**: protects one player per night (self-protect only if `DoctorCanSelfProtect`).
- **Seer**: inspects one player per night; result is sent privately to that Seer only.
- **Witch**: one Heal potion and one Poison potion, each usable once **per game**. Heal requires the
  wolves' target to already be locked. If `WitchSinglePotionPerNight`, at most one potion per night;
  otherwise both may be used the same night. `PassWitch` explicitly declines.

### 4. Death resolution & Hunter revenge

`DeathResolver.Resolve` expands an initial set of victims to a fixpoint: if either paired lover dies,
the other dies too (chained), and any newly-dead Hunter with an unused ability is queued for revenge.
`SubmitHunterRevengeShot`/`PassHunterRevenge` resolve that queue one Hunter at a time — a revenge kill
can itself cascade (a lover, or another Hunter) — and only once the queue is empty does the game
re-check the win condition and resume the phase transition (Night → Day, or Day → next Night) that was
paused for it.

### 5. Day voting resolution

Every living player casts one `CastVote` (or abstains); voting closes via `CloseVoting` once all
living players have voted, or the host closes it early (or, on the frontend, once its own
`VotingDeadlineUtc` countdown expires — see below). A lynch requires the top vote-getter to have
**at least half of the currently-alive players'** votes, and no tie with the runner-up; otherwise no
one is lynched and the game moves straight into the next Night. This is a real majority requirement,
not a plurality — three votes out of ten scattered ballots no longer hangs someone just for having the
most.

`GameSettings.DiscussionDurationSeconds`/`VotingDurationSeconds` (defaults 120s/30s) are purely a
shared clock: `GameState.DayStartedAtUtc`/`VotingStartedAtUtc` plus the configured duration produce
`GameStateResponse.DiscussionDeadlineUtc`/`VotingDeadlineUtc` for every client to render the same
countdown from. The backend itself never enforces either deadline — `AdvanceToVoting`/`CloseVoting`
still only fire on an explicit host request (`GameCommandSupport.ValidateHost` + `ValidatePhase`
reject anyone/anything else), it's just that the frontend's host client now fires that request
automatically once its own countdown reaches 0:00, rather than only on a manual click.

### 6. Win conditions

Checked every time a death round fully settles (no Hunter revenge left pending):

| Winner | Condition |
|---|---|
| Lovers | overrides both below — the two paired lovers are the last two players alive |
| Villagers | no werewolves remain alive |
| Werewolves | alive werewolves ≥ alive non-werewolves |

## Commands

All commands carry a `RoomCode` (except `CreateLobby`, which allocates one). Unless noted, these are
Wolverine message handlers — invoke via `IMessageBus.InvokeAsync`/`PublishAsync`, or over HTTP where an
endpoint is listed.

### Lobby

| Command | Guard | Result |
|---|---|---|
| `CreateLobby` | — | new Lobby stream + room code |
| `JoinLobby` | lobby open | `PlayerJoinedLobby` |
| `LeaveLobby` | present | `PlayerLeftLobby` (+`HostTransferred` if host left) |
| `KickPlayer` | host; target ≠ host | `PlayerKickedFromLobby` |
| `SetReady` | present | `PlayerReadyStatusChanged` |
| `UpdateRoleDistribution` | host | `RoleDistributionUpdated` |
| `UpdateGameSettings` | host | `GameSettingsUpdated` |
| `StartGame` | host; ≥ min players; roles valid; all ready or force-start | closes Lobby, starts Game |
| `CancelLobby` | host | `LobbyCancelled` |

### Game — night

| Command | Guard |
|---|---|
| `SubmitCupidPairing` | night 1; alive Cupid; not yet paired |
| `SubmitWerewolfVote` | alive Werewolf; living non-wolf target |
| `SubmitDoctorProtection` | alive Doctor; not yet acted this night |
| `SubmitSeerInspection` | alive Seer; not yet acted; target ≠ self |
| `UseWitchHealPotion` | alive Witch; heal potion unused; wolves' target locked |
| `UseWitchPoisonPotion` | alive Witch; poison potion unused; living target |
| `PassWitch` | alive Witch; not yet acted |
| `SubmitHunterRevengeShot` | player is head of the pending-revenge queue; living target |
| `PassHunterRevenge` | player is head of the pending-revenge queue |

### Game — day

| Command | Guard |
|---|---|
| `AdvanceToVoting` | host; phase = DayDiscussion |
| `CastVote` | alive voter; living target or abstain |
| `CloseVoting` | host; phase = DayVoting |

## HTTP endpoints

Only the player-facing entry points that need a typed HTTP response have endpoints; the rest of the
Game/Lobby commands above are invoked as Wolverine messages (e.g. from a SignalR-connected client via
the message bus, or from a future thin HTTP wrapper).

| Route | Body | Response |
|---|---|---|
| `POST /api/v1/lobby` *(CreateLobby)* | `{ hostPlayerId, hostDisplayName }` | `{ roomCode }` |
| `POST /api/v1/lobby/join` *(JoinLobby)* | `{ roomCode, playerId, displayName }` | 200 |
| `POST /api/v1/lobby/start` *(StartGame)* | `{ roomCode, requestedBy, forceStart }` | `{ gameId, roomCode }` |

OpenAPI/Scalar docs are served at `/scalar` in development (see `EndpointsConfiguration`).

## Real-time notifications (SignalR)

Committed Game events (`GameStarted`, `NightStarted`, `DayStarted`, `VotingStarted`, `PlayerDied`,
`PlayerLynched`, `SeerInspectionPerformed`, `GameEnded`) are forwarded from Marten's async daemon to
Wolverine handlers (`Werewolf/Notifications/PlayerNotification.cs`), which push a `PlayerNotification`
over SignalR to one of two group scopes:

- `room:{roomCode}` — broadcasts (deaths, phase changes, game end).
- `room:{roomCode}:player:{playerId:N}` — role-scoped (e.g. a Seer's own inspection result). A villager
  is never in a werewolf's, or another player's, private group, so secrets never leak client-side.

Clients connect to the hub at `/hubs/werewolf` and send a `JoinGameRoom { roomCode, playerId }` message
over that connection (`LeaveGameRoom` to unsubscribe) to join the relevant group(s). `RevealRoleOnDeath`
(a per-lobby game setting) controls whether `player.died`/`player.lynched` notifications include the
dead player's role.

## Read models

- **`RoomLobbyView`** — public lobby state (players, settings, role distribution).
- **`PlayerGameView`** — one document per `(GameId, PlayerId)`, role-scoped: a player only ever sees
  events they're entitled to (their own Seer results, their own werewolf-team votes if they're a wolf).
- **`GameLogView`** — spoiler-safe, human-readable activity feed for the whole game.

## Run

```bash
dotnet restore
dotnet build
dotnet run --project src/Application/Application.csproj
```

Requires a `ConnectionStrings:exploratory-conversations` Postgres connection string in configuration
(see `src/Application/appsettings*.json`); schema objects are created automatically in Development.
