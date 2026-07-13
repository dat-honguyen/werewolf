# Werewolf Backend — Domain Design Plan (Functionality, State Machine, Aggregates)

## Context

Starting a new .NET backend for a Werewolf (Mafia-style social deduction) party game in the currently-empty directory `/home/dat98/s/lion/werewolf`. The stack is fixed: .NET, event sourcing, **WolverineFx** for command handling/messaging, **MartenDB** for the event store/projections (Postgres-backed). Infra, hosting, DI wiring, and config are explicitly **out of scope** — the user will handle that separately. This plan's job is to nail down *what the game does*, *how it flows as a state machine*, and *how the event-sourced aggregates are shaped* so that implementation can start directly from it.

Local reference checkouts exist at `/home/dat98/s/lion/marten` (tag `V9.8.2`) and `/home/dat98/s/lion/wolverine` (tag `V6.17.0`) — the design below was verified against these (Natural Keys, `[AggregateHandler]`, `MartenOps.StartStream`, `MultiStreamProjection`, event subscriptions all confirmed to exist as described).

Key decisions locked in with the user:
- **Extended role set**: Werewolf, Villager, Seer, Doctor, Hunter, Witch, Cupid.
- **Split aggregates**: separate `Lobby` and `Game` aggregates/streams, both keyed by the human-shareable **room code**.
- **Notification seam included** at design level (where Wolverine handlers would turn events into player-facing updates), but no SignalR/transport wiring.

---

## 0. Foundational decisions

### Stream identity keyed by room code
Use Marten's `[NaturalKey]` / `[NaturalKeySource]` (confirmed present in `marten/docs/events/natural-keys.md`). Keep default `StreamIdentity.AsGuid`; both `LobbyState` and `GameState` independently declare `RoomCode` as their natural key, so `"PQXR7K"` resolves independently within each aggregate type's own lookup table — no collision between Lobby's and Game's use of the same code.

```csharp
public record RoomCode(string Value); // 6 chars, no ambiguous glyphs (0/O/1/I)

public class LobbyState
{
    public Guid Id { get; set; }
    [NaturalKey] public RoomCode RoomCode { get; set; } = null!;
    [NaturalKeySource] public void Apply(LobbyCreated e) { Id = e.LobbyId; RoomCode = e.RoomCode; }
}
```

Reads/writes use `session.Events.FetchForWriting<LobbyState, RoomCode>(roomCode)` / `FetchLatest<GameState, RoomCode>(roomCode)`. Fallback if Natural Keys prove awkward at implementation time: `StreamIdentity.AsString` with deterministic ids (`"lobby:{code}"` / `"game:{code}"`).

Room codes are generated server-side (never client-supplied) in the `CreateLobby` handler, with a bounded regenerate-on-collision retry loop.

### No wall-clock infra needed for sequencing
Every "how do we sequence sub-steps" question resolves the same way: **each command handler recomputes a completeness predicate over the folded aggregate state and, if satisfied, appends the phase-advance event(s) in the same transaction** (Wolverine's documented "process manager via handlers" pattern — confirmed in `wolverine/docs/guide/durability/marten/process-manager-via-handlers.md`). No timers, sagas, or background jobs. This is the core mechanism behind night sub-phase sequencing, vote-closing, and win-condition evaluation.

---

## A. Core game functionality (first working slice)

1. Room creation — host creates a room, gets back a shareable room code.
2. Join/leave lobby by room code + display name.
3. Host lobby management — kick player, configure role distribution and game settings (reveal-on-death, doctor self-protect rule, werewolf-consensus rule, witch single-potion-per-night rule, min players).
4. Ready-up per player; host starts once constraints satisfied (or force-starts if allowed).
5. Start game — shuffle/assign roles, bridge Lobby → Game.
6. Role reveal-to-self only (never broadcast).
7. Night phase, per role: Cupid pairs lovers (night 1 only), Werewolves collectively pick a victim, Doctor protects one player, Seer inspects one player (result private), Witch has one-time Heal (save wolves' target) and Poison (kill anyone, bypasses Doctor protection by default) potions.
8. Night resolution — combine wolf kill (unless saved) + poison, expand lover-linked deaths to a fixpoint, flag newly-dead Hunters for revenge.
9. Hunter's revenge — on death (night or day), if unused, Hunter gets one interrupt shot that can itself chain further deaths.
10. Day discussion — pure phase marker (no chat protocol), advanced by host command.
11. Day voting/lynching — one vote per living player (changeable), auto-closes when all alive have voted, or host force-closes.
12. Death resolution & reveal — majority lynch (tie/no-majority → no lynch), same Hunter/lover cascade as night.
13. Win-condition check (explicit step, run whenever a death round fully settles): Villagers win when no wolves remain; Werewolves win when alive wolves ≥ alive non-wolves; Lovers win (overrides both) when the two paired lovers are the last two alive.
14. Game end — full role reveal, `GameEnded`, aggregate becomes terminal.

---

## B. State machines

### B.1 Lobby
```csharp
public enum LobbyStatus { Open, Starting, Closed, Cancelled }
```
`Open → Starting → Closed` happy path; `Cancelled` reachable only from `Open`. Transitions:

| From | Command | Guard | Event(s) | To |
|---|---|---|---|---|
| Open | `JoinLobby` | not full, not already present | `PlayerJoinedLobby` | Open |
| Open | `LeaveLobby` | present | `PlayerLeftLobby` (+`HostTransferred` if host left) | Open |
| Open | `KickPlayer` | requester==host, target≠host | `PlayerKickedFromLobby` | Open |
| Open | `SetReady` | present | `PlayerReadyStatusChanged` | Open |
| Open | `UpdateRoleDistribution` / `UpdateGameSettings` | requester==host | `RoleDistributionUpdated`/`GameSettingsUpdated` | Open |
| Open | `StartGame` | host; players≥5; role counts valid; wolves≥1 and <half; all ready (or force-start) | `GameStarting` → bridge to new Game stream (`GameStarted`,`RolesAssigned`) → `LobbyClosed` | Closed |
| Open | `CancelLobby` | host | `LobbyCancelled` | Cancelled |
| terminal states | any | — | rejected | — |

### B.2 Game — phases
```csharp
public enum GamePhase { RoleAssignment, Night, DayDiscussion, DayVoting, DayResolution, GameOver }
```
Hunter's revenge is **not** a phase — it's an orthogonal guard (`PendingHunterRevenge` queue non-empty) that blocks phase-advancing commands regardless of whether it interrupts Night or DayResolution.

### B.3 Night sub-phase sequencing (the key design answer)
Night is one event-sourced phase; "sub-steps" are a dependency graph checked by a completeness predicate, not a linear FSM:
- **Cupid**: independent, only matters on night 1.
- **Werewolves**: each vote event's handler checks "have all alive wolves voted / has consensus converged?" — if so, appends `WerewolfTargetLocked` in the same transaction.
- **Doctor**, **Seer**: fully independent.
- **Witch**: heal decision is *guarded* on `WerewolfTargetLocked != null` (command rejected if wolves haven't locked yet — client retries, no waiting/polling infra).

Every night-action handler ends by re-checking `NightChecklist.IsComplete(state)` (walks all five role slots, treating a slot satisfied if no living holder exists, ability already exhausted, or action/explicit-pass recorded) and, if true, appends `NightResolved` in the same append. Explicit "no action" events (e.g. `WitchPassed`) are required so the checklist has an observable "done" signal instead of waiting forever.

### B.4 Death resolution & win-check (shared pure logic)
One reusable `DeathResolver.Resolve(state, initialVictims)` function expands lover-linked deaths to a fixpoint and flags newly-dead Hunters with an unused ability — used identically by night kills, day lynches, and Hunter-revenge shots (re-entrant, so a shot killing another Hunter correctly re-queues them). `WinConditionEvaluator.Evaluate(state)` runs only once `PendingHunterRevenge` is empty, since a revenge shot can itself flip the outcome.

### B.5 Full Game phase transition table

| Phase | Command | Guard | Event(s) | Next |
|---|---|---|---|---|
| *(bridge)* | — | from Lobby `StartGame` | `GameStarted`,`RolesAssigned`,`NightStarted(1)` | RoleAssignment→**Night** |
| Night | `SubmitCupidPairing` | night 1; alive Cupid, not yet paired | `CupidPairedLovers` | Night (auto-check) |
| Night | `SubmitWerewolfVote` | alive Werewolf; valid target | `WerewolfVoteCast` (+`WerewolfTargetLocked` on consensus) | Night (auto-check) |
| Night | `SubmitDoctorProtection` | alive Doctor; not yet acted | `DoctorProtectionChosen` | Night (auto-check) |
| Night | `SubmitSeerInspection` | alive Seer; not yet acted; target≠self | `SeerInspectionPerformed` | Night (auto-check) |
| Night | `UseWitchHealPotion` | alive Witch; has potion; `WerewolfTargetLocked!=null`; not yet acted | `WitchHealUsed` | Night (auto-check) |
| Night | `UseWitchPoisonPotion` | alive Witch; has potion; single-potion rule | `WitchPoisonUsed` | Night (auto-check) |
| Night | `PassWitch` | alive Witch; not yet acted | `WitchPassed` | Night (auto-check) |
| Night *(auto)* | — | checklist complete | `NightResolved` (+cascade) | if Hunters pending→blocked; else if win→`GameEnded`→GameOver; else `DayStarted`→**DayDiscussion** |
| blocked (Night/DayResolution) | `SubmitHunterRevengeShot`/`PassHunterRevenge` | Hunter head of queue | `HunterRevengeShotFired`/`Declined` (+cascade) | resumes pending transition once queue empty |
| DayDiscussion | `AdvanceToVoting` | host (or configurable) | `VotingStarted` | **DayVoting** |
| DayVoting | `CastVote` | alive; valid target/abstain | `VoteCast` (+`VotingClosed`+tally when all alive voted) | →**DayResolution** (auto) |
| DayVoting | `CloseVoting` | host escape hatch | `VotingClosed`+tally | **DayResolution** |
| DayResolution *(auto)* | — | — | `PlayerLynched`/`NoOneLynched` (+cascade) | if Hunters pending→blocked; else if win→GameOver; else `NightStarted(n+1)`→**Night** |
| GameOver | any | — | rejected (terminal) | — |

---

## C. Aggregate & event design

### C.1 Lobby aggregate
Identity: Guid stream id, `RoomCode` as `[NaturalKey]`.

Events: `LobbyCreated`, `PlayerJoinedLobby`, `PlayerLeftLobby`, `PlayerKickedFromLobby`, `HostTransferred`, `PlayerReadyStatusChanged`, `RoleDistributionUpdated`, `GameSettingsUpdated`, `GameStarting`, `LobbyClosed`, `LobbyCancelled`.

Commands: `CreateLobby`, `JoinLobby`, `LeaveLobby`, `KickPlayer`, `SetReady`, `UpdateRoleDistribution`, `UpdateGameSettings`, `StartGame`, `CancelLobby`.

`CreateLobby` is a **plain start handler** (not `[AggregateHandler]` — that's only for continuing an existing stream) that generates+dedupes the room code and returns `MartenOps.StartStream<LobbyState>(...)`. All other commands are standard `[AggregateHandler]` continue-handlers resolving via `RoomCode`.

### C.2 Game aggregate
Identity: Guid stream id, `RoomCode` as `[NaturalKey]` (independent lookup table from Lobby's).

State shape: `GameState` with `Phase`, `NightNumber`/`DayNumber`, `Players: Dictionary<Guid, GamePlayer>` (role, status, per-ability-used flags), `CurrentNight: NightActionsState`, `CurrentVote: DayVoteState`, `Lovers`, `PendingHunterRevenge: Queue<Guid>`, `Result`.

Events (grouped): setup (`GameStarted`, `RolesAssigned`, `NightStarted`), night actions (`CupidPairedLovers`, `WerewolfVoteCast`, `WerewolfTargetLocked`, `DoctorProtectionChosen`, `SeerInspectionPerformed`, `WitchHealUsed`, `WitchPoisonUsed`, `WitchPassed`), resolution (`NightResolved`, `HunterRevengePending`, `HunterRevengeShotFired`, `HunterRevengeDeclined`), day (`DayStarted`, `VotingStarted`, `VoteCast`, `VotingClosed`, `LynchTargetDetermined`, `NoLynchOccurred`, `PlayerLynched`), end (`GameEnded`).

Commands mirror the transition table in B.5 1:1, each carrying `RoomCode` + actor id + `Version` (for optimistic concurrency).

### C.3 Cross-aggregate bridge: Lobby.StartGame → Game stream
`StartGame`'s handler appends to the **existing** Lobby stream (`GameStarting`, `LobbyClosed`) and **starts a new** Game stream (`GameStarted`, `RolesAssigned`, `NightStarted(1)`) atomically within one `SaveChangesAsync` — Marten supports multi-stream atomic commits (confirmed via the `TransferMoney` sample pattern in `wolverine/docs/guide/durability/marten/event-sourcing.md`). No saga/distributed-transaction machinery needed.

**Flag for implementation-time verification**: combining an existing-stream `[WriteAggregate]` append with a *new* stream's `MartenOps.StartStream` return in a single handler composes two patterns documented separately — confirm this exact combination codegens correctly against the pinned Wolverine version; fallback is splitting into two handlers using manual `IDocumentSession` + explicit outbox enrollment.

### C.4 Concurrency & idempotency
- Optimistic concurrency is automatic via `FetchForWriting`/`[AggregateHandler]` version checks; configure a Wolverine retry-with-cooldown policy on `ConcurrencyException` at wiring time (later).
- Double voting: `VoteCast` folds into a `Dictionary<VoterId, TargetId>` — re-voting naturally overwrites, no special-casing.
- Duplicate night-action submission: explicitly guarded per role (reject with domain error) rather than silently ignored.
- `StartGame`/`CreateLobby` races: handled by the room-code collision retry loop and the `Status != Open` guard respectively, safe under concurrency because the loser's fetch sees updated state or hits `ConcurrencyException`.

---

## D. Read models, projections & the notification seam

Three Marten projections, kept separate from write-side state to enforce role-scoped secrecy:

1. **`RoomLobbyView`** — single-stream projection off Lobby, public (no secrets pre-game), `SnapshotLifecycle.Inline`.
2. **`PlayerGameView`** — role-scoped, one document per `(GameId, PlayerId)`, built as a `MultiStreamProjection<PlayerGameView, PlayerViewId>` (confirmed present in Marten source) fanning a single Game stream to many documents via `Identities<TEvent>()`. Public events (deaths, phase changes) fan out to everyone; secret events (`SeerInspectionPerformed`, `WerewolfVoteCast`) fan out only to the entitled player(s) — **this is the mechanism that keeps a villager from ever seeing who the werewolves are**. `SnapshotLifecycle.Inline` for immediate feedback.
3. **`GameLogView`** — public, spoiler-safe activity feed; secret-carrying events are redacted/omitted; human-readable log lines. `SnapshotLifecycle.Async` is fine since nothing gates on it.

**Notification seam** (design only, no transport): Wolverine's Marten event-subscription integration (`PublishEventsToWolverine`, confirmed in `wolverine/docs/guide/durability/marten/subscriptions.md`) streams committed Game events to ordinary Wolverine handlers via the async daemon, decoupled from the write transaction. Handlers translate events into `PlayerNotification.Broadcast(RoomCode, Kind, Payload)` or `PlayerNotification.ToPlayer(PlayerId, Kind, Payload)` messages — e.g. `PlayerLynched` → broadcast to the room, `SeerInspectionPerformed` → targeted to just that Seer. The actual delivery adapter (SignalR hub group per room code, etc.) is **not** designed here — it's a later consumer of `PlayerNotification` messages.

---

## E. Extension points (not built now, design accommodates them)

- Additional roles (Bodyguard, Mayor, Tanner) slot into `Role` enum + `RoleDistribution` + `NightChecklist` without touching the phase machine.
- Alternate win conditions (Tanner-wins-by-lynch) extend `WinConditionEvaluator` and `WinningFaction`, same settle-point.
- Multiple holders of currently-unique roles: only touches `StartGame`'s validation guard (event/command shapes already support N holders, since Werewolf already is).
- Timed phases later plug in as a scheduled message calling the *same* `AdvanceToVoting`/`CloseVoting` commands a human host calls today — zero state-machine change.

---

## F. Suggested project/file layout

```
Vertical slice architect
```

### Critical files to implement first (in dependency order)
1. `Werewolf.Domain/Game/GameState.cs` — the folded state everything else validates against.
2. `Werewolf.Domain/Game/NightChecklist.cs` — the completeness predicate that is the whole answer to sequencing without wall-clock infra.
3. `Werewolf.Domain/Game/DeathResolver.cs` — shared pure cascade logic reused by night/day/revenge handlers.
4. `Werewolf.Domain/Lobby/LobbyHandlers/StartGameHandler.cs` — the cross-aggregate bridge; verify the multi-stream combination against the pinned Wolverine version first.
5. `Werewolf.ReadModels/PlayerGameView.cs` (+projection) — the role-scoped secrecy boundary; get this wrong and werewolves leak to villagers.

---

## Verification plan

Since this is domain design with no infra yet, "testing end-to-end" means unit-level verification of the state machine logic once implemented:
1. Unit tests around `NightChecklist.IsComplete` for every combination of alive/dead role holders and exhausted abilities.
2. Unit tests around `DeathResolver.Resolve` covering: simple kill, lover-chain cascade (including a lover who is also a Hunter), Hunter revenge re-entrancy.
3. Unit tests around `WinConditionEvaluator` for all four outcomes (villagers/werewolves/lovers win, game continues), including the "revenge shot flips the outcome" case.
4. Integration-style tests using Marten's `InMemoryDaemon`/test harness to append a full game's event sequence through the aggregate handlers and assert the final `GameState`/`PlayerGameView` shapes — no real Postgres required for this since Marten supports in-memory testing modes.
5. Once `Werewolf.Api` project scaffolding exists (later), a smoke test: create lobby → join 5+ players → start → play one full night/day cycle via HTTP/command dispatch → assert `GameEnded` reachable.
