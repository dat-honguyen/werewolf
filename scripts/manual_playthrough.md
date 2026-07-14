# Manual playthrough — step-by-step HTTP calls

A copy-pasteable `curl` walkthrough for driving a full game by hand. Base URL assumed
`http://localhost:5080` — bring the app up first:

```bash
.claude/skills/run-werewolf/driver.sh up
```

Swap in your own room code and player ids as you go (any valid UUID works for a player id — it
doesn't need to be registered anywhere first).

## 0. Reference: list all roles

```bash
curl -sS http://localhost:5080/api/v1/roles
```

## 1. Create the lobby (you become host)

```bash
curl -sS -X POST http://localhost:5080/api/v1/lobby \
  -H "Content-Type: application/json" \
  -d '{"hostPlayerId":"11111111-1111-1111-1111-111111111111","hostDisplayName":"Alice"}'
# => {"roomCode":"XXXXXX"}
```

Save the `roomCode` — every call below needs it.

## 2. Join more players (need 5+ total including host)

```bash
curl -sS -X POST http://localhost:5080/api/v1/lobby/join -H "Content-Type: application/json" \
  -d '{"roomCode":"XXXXXX","playerId":"22222222-2222-2222-2222-222222222222","displayName":"Bob"}'
curl -sS -X POST http://localhost:5080/api/v1/lobby/join -H "Content-Type: application/json" \
  -d '{"roomCode":"XXXXXX","playerId":"33333333-3333-3333-3333-333333333333","displayName":"Carol"}'
curl -sS -X POST http://localhost:5080/api/v1/lobby/join -H "Content-Type: application/json" \
  -d '{"roomCode":"XXXXXX","playerId":"44444444-4444-4444-4444-444444444444","displayName":"Dave"}'
curl -sS -X POST http://localhost:5080/api/v1/lobby/join -H "Content-Type: application/json" \
  -d '{"roomCode":"XXXXXX","playerId":"55555555-5555-5555-5555-555555555555","displayName":"Eve"}'
```

## 3. (Optional) Set a custom role distribution

Default is Werewolf/Seer/Doctor/Witch/Hunter/Cupid = 1 each, filling the rest as Villager. To
include a Tanner instead, e.g.:

```bash
curl -sS -X POST http://localhost:5080/api/v1/lobby/roles -H "Content-Type: application/json" \
  -d '{"roomCode":"XXXXXX","requestedBy":"11111111-1111-1111-1111-111111111111","distribution":{"Werewolf":1,"Tanner":1}}'
```

## 4. Everyone marks ready

```bash
for pid in 11111111-1111-1111-1111-111111111111 22222222-2222-2222-2222-222222222222 \
           33333333-3333-3333-3333-333333333333 44444444-4444-4444-4444-444444444444 \
           55555555-5555-5555-5555-555555555555; do
  curl -sS -X POST http://localhost:5080/api/v1/lobby/ready -H "Content-Type: application/json" \
    -d "{\"roomCode\":\"XXXXXX\",\"playerId\":\"$pid\",\"isReady\":true}"
done
```

## 5. Start the game

```bash
curl -sS -X POST http://localhost:5080/api/v1/lobby/start -H "Content-Type: application/json" \
  -d '{"roomCode":"XXXXXX","requestedBy":"11111111-1111-1111-1111-111111111111","forceStart":false}'
```

## 6. Check state to see everyone's role

```bash
curl -sS http://localhost:5080/api/v1/game/XXXXXX
```

Now you know who's Werewolf/Seer/Doctor/Witch/Hunter/Cupid/Tanner — use those player ids below.

## 7. Night actions (only call the ones matching roles present; night 1 also has Cupid)

```bash
# Cupid — night 1 only
curl -sS -X POST http://localhost:5080/api/v1/game/cupid -H "Content-Type: application/json" \
  -d '{"roomCode":"XXXXXX","playerId":"<cupidId>","firstPlayerId":"<id1>","secondPlayerId":"<id2>"}'

# Seer (response only ever tells you werewolf-or-not, never the exact role)
curl -sS -X POST http://localhost:5080/api/v1/game/seer/inspect -H "Content-Type: application/json" \
  -d '{"roomCode":"XXXXXX","playerId":"<seerId>","targetPlayerId":"<targetId>"}'

# Doctor (rejected with 400 if targetId is the same player you protected last night)
curl -sS -X POST http://localhost:5080/api/v1/game/doctor/protect -H "Content-Type: application/json" \
  -d '{"roomCode":"XXXXXX","playerId":"<doctorId>","targetPlayerId":"<targetId>"}'

# Werewolf (by default every living wolf must vote for a kill and can never target
# another werewolf; single wolf auto-locks). Two per-game settings relax this:
# WerewolfCanVoteNoKill (omit targetPlayerId to vote no-kill) and
# WerewolfCanTargetWerewolf (a werewolf becomes a valid target for other werewolves --
# a werewolf can never vote for themselves, regardless of settings). Every living
# werewolf gets a private "werewolf.vote" push as each vote lands, and "werewolf.locked"
# once the target (or no-kill) locks in.
curl -sS -X POST http://localhost:5080/api/v1/game/werewolf/vote -H "Content-Type: application/json" \
  -d '{"roomCode":"XXXXXX","playerId":"<wolfId>","targetPlayerId":"<targetId>"}'

# Witch — pick ONE of these three
curl -sS -X POST http://localhost:5080/api/v1/game/witch/heal -H "Content-Type: application/json" \
  -d '{"roomCode":"XXXXXX","playerId":"<witchId>"}'
curl -sS -X POST http://localhost:5080/api/v1/game/witch/poison -H "Content-Type: application/json" \
  -d '{"roomCode":"XXXXXX","playerId":"<witchId>","targetPlayerId":"<targetId>"}'
curl -sS -X POST http://localhost:5080/api/v1/game/witch/pass -H "Content-Type: application/json" \
  -d '{"roomCode":"XXXXXX","playerId":"<witchId>"}'
```

Night resolves automatically once every living role has acted — check with step 6 until
`"phase":"DayDiscussion"`.

## 8. Advance to voting (host only, explicit)

```bash
curl -sS -X POST http://localhost:5080/api/v1/game/voting/advance -H "Content-Type: application/json" \
  -d '{"roomCode":"XXXXXX","requestedBy":"<hostId>"}'
```

## 9. Everyone votes (or pass `"targetPlayerId":null` to abstain)

```bash
curl -sS -X POST http://localhost:5080/api/v1/game/vote -H "Content-Type: application/json" \
  -d '{"roomCode":"XXXXXX","voterPlayerId":"<voterId>","targetPlayerId":"<targetId>"}'
```

Repeat for every living player. Voting auto-closes once everyone alive has voted (no separate
close call needed). Every vote also broadcasts live as a "vote.cast" SignalR push to the whole
room, not just once voting closes.

## 10. Hunter revenge (only if the log/state shows a pending hunter)

```bash
curl -sS -X POST http://localhost:5080/api/v1/game/hunter/shoot -H "Content-Type: application/json" \
  -d '{"roomCode":"XXXXXX","playerId":"<hunterId>","targetPlayerId":"<targetId>"}'
# or
curl -sS -X POST http://localhost:5080/api/v1/game/hunter/pass -H "Content-Type: application/json" \
  -d '{"roomCode":"XXXXXX","playerId":"<hunterId>"}'
```

## 11. Check state / read the log at any point

```bash
curl -sS http://localhost:5080/api/v1/game/XXXXXX
curl -sS http://localhost:5080/api/v1/game/XXXXXX/log
```

Repeat steps 7–10 for each subsequent night/day until `state.phase == "GameOver"` and
`state.result.winningFaction` is set.
