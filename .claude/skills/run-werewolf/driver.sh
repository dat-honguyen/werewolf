#!/usr/bin/env bash
# Driver for the Werewolf backend (Wolverine.Http + Marten API on ASP.NET Core).
# Run from the repo root: .claude/skills/run-werewolf/driver.sh <command> [args...]
#
# Commands:
#   up          Start Postgres (docker compose), build, launch the app in the
#               background, wait for /health/ready, print the PID and base URL.
#   down        Stop the app (by PID file) and leave Postgres running.
#   smoke       Run the golden-path HTTP flow against a running instance
#               (create lobby -> join second player -> verify via DB).
#   status      Show whether the app is listening and Postgres container state.
#
# Env overrides: PORT (default 5080), DOTNET (default /home/dat98/.dotnet/dotnet),
# COMPOSE (default: podman compose, since `docker` on this box is a zsh ALIAS
# for podman -- invisible to this non-interactive script; see Gotchas)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
APP_DIR="$ROOT/src/Application"
PORT="${PORT:-5080}"
DOTNET="${DOTNET:-dotnet}"
COMPOSE="${COMPOSE:-podman compose}"
PID_FILE="$ROOT/.claude/skills/run-werewolf/.app.pid"
LOG_FILE="$ROOT/.claude/skills/run-werewolf/.app.log"
BASE_URL="http://localhost:${PORT}"

cmd_up() {
  echo "== $COMPOSE up -d (Postgres) =="
  (cd "$ROOT" && $COMPOSE up -d) || true  # podman-compose exits nonzero if container already exists; harmless

  echo "== build =="
  "$DOTNET" build "$APP_DIR/Application.csproj"

  if [ -f "$PID_FILE" ] && kill -0 "$(cat "$PID_FILE")" 2>/dev/null; then
    echo "already running (pid $(cat "$PID_FILE"))"
    return 0
  fi

  # Free the port if a stray previous run is still bound to it (the apphost
  # process is named "Application", NOT "dotnet" or "Application.dll" --
  # `pgrep -f Application.dll` will NOT find it).
  local stale
  stale=$(ss -ltnp 2>/dev/null | grep ":${PORT} " | grep -oP 'pid=\K[0-9]+' | head -1 || true)
  if [ -n "$stale" ]; then
    echo "killing stale process on port $PORT (pid $stale)"
    kill "$stale" 2>/dev/null || true
    sleep 2
  fi

  echo "== launch (background) =="
  (
    cd "$APP_DIR"
    ASPNETCORE_ENVIRONMENT=Development exec "$DOTNET" run --no-launch-profile --urls "$BASE_URL"
  ) > "$LOG_FILE" 2>&1 &
  local wrapper_pid=$!
  echo "$wrapper_pid" > "$PID_FILE"

  echo "== waiting for /health/ready =="
  for _ in $(seq 1 30); do
    if curl -sf "$BASE_URL/health/ready" > /dev/null 2>&1; then
      echo "up: $BASE_URL (pid $wrapper_pid, log: $LOG_FILE)"
      return 0
    fi
    if ! kill -0 "$wrapper_pid" 2>/dev/null; then
      echo "process exited early; see $LOG_FILE" >&2
      tail -40 "$LOG_FILE" >&2
      exit 1
    fi
    sleep 1
  done
  echo "timed out waiting for health check; see $LOG_FILE" >&2
  tail -40 "$LOG_FILE" >&2
  exit 1
}

cmd_down() {
  # The real listener is the "Application" apphost process, a child of the
  # `dotnet run` wrapper recorded in PID_FILE -- kill by port, not by PID_FILE,
  # or the app survives and the next `up` fails with "address already in use".
  local listener
  listener=$(ss -ltnp 2>/dev/null | grep ":${PORT} " | grep -oP 'pid=\K[0-9]+' | head -1 || true)
  if [ -n "$listener" ]; then
    kill "$listener"
    echo "stopped (pid $listener)"
  else
    echo "nothing listening on $PORT"
  fi
  rm -f "$PID_FILE"
}

cmd_status() {
  echo "== postgres =="
  podman ps --filter "name=postgres" --format "{{.Names}} {{.Status}}" || true
  echo "== app =="
  if curl -sf "$BASE_URL/health/ready" > /dev/null 2>&1; then
    echo "up: $BASE_URL"
  else
    echo "down"
  fi
}

cmd_smoke() {
  echo "== POST /api/v1/lobby (create) =="
  local create_resp room_code
  create_resp=$(curl -sS -X POST "$BASE_URL/api/v1/lobby" \
    -H "Content-Type: application/json" \
    -d '{"hostPlayerId":"11111111-1111-1111-1111-111111111111","hostDisplayName":"Alice"}')
  echo "$create_resp"
  room_code=$(echo "$create_resp" | grep -oP '"roomCode":"\K[^"]+')
  if [ -z "$room_code" ]; then
    echo "FAIL: no roomCode in response" >&2
    exit 1
  fi

  echo "== POST /api/v1/lobby/join (second player) =="
  local join_status
  join_status=$(curl -sS -o /dev/null -w '%{http_code}' -X POST "$BASE_URL/api/v1/lobby/join" \
    -H "Content-Type: application/json" \
    -d "{\"roomCode\":\"$room_code\",\"playerId\":\"22222222-2222-2222-2222-222222222222\",\"displayName\":\"Bob\"}")
  echo "HTTP $join_status"
  if [ "$join_status" != "204" ]; then
    echo "FAIL: expected 204 from join" >&2
    exit 1
  fi

  # NOTE: LobbyState/GameState are registered as LiveStreamAggregation (see
  # WerewolfMartenModule.cs) -- by design this is NEVER persisted as a
  # document (mt_doc_lobbystate stays empty forever; that's not a bug, don't
  # "fix" it). The actual source of truth is the event stream, and the
  # queryable read model is mt_doc_roomlobbyview (a separate Inline/Async
  # projection). Verify against those instead.
  echo "== verify via Postgres (event stream) =="
  local events
  events=$(podman exec werewolf_postgres_1 psql -U werewolf -d werewolf -tAc "
    select string_agg(e.type, ',' order by e.seq_id) from public.mt_events e
    join public.mt_natural_key_lobbystate nk on nk.stream_id = e.stream_id
    where nk.natural_key_value = '$room_code';")
  echo "events: $events"
  if [ "$events" != "lobby_created,player_joined_lobby" ]; then
    echo "FAIL: expected lobby_created,player_joined_lobby, got: $events" >&2
    exit 1
  fi

  # RoomLobbyView is Async (Wolverine-managed subscription distribution, not
  # Marten's daemon -- see the "async daemon is disabled" log line, which is
  # a red herring: it refers to Marten's own daemon, not this path). It
  # catches up within ~1s in practice; poll briefly instead of asserting
  # immediately, or this flakes on a fast machine/slow one alike.
  echo "== verify via Postgres (mt_doc_roomlobbyview read model, eventually consistent) =="
  local players=""
  for _ in $(seq 1 10); do
    # `|| true`: on a freshly created DB the table can briefly not exist yet
    # (AutoCreate.All lazily creates projection-only tables on first async
    # write, not necessarily by the time /health/ready passes) -- that's a
    # real startup race, not a query bug. Swallow and keep polling.
    players=$(podman exec werewolf_postgres_1 psql -U werewolf -d werewolf -tAc \
      "select data->'Players' from public.mt_doc_roomlobbyview where data->>'RoomCode' = '$room_code';" 2>/dev/null || true)
    if [[ "$players" == *"Bob"* ]]; then
      break
    fi
    sleep 0.5
  done
  echo "$players"
  if [[ "$players" != *"Bob"* ]]; then
    echo "FAIL: RoomLobbyView read model missing or doesn't show Bob after polling" >&2
    exit 1
  fi

  echo "PASS: room $room_code has both events appended and read model shows both players"
}

case "${1:-}" in
  up) cmd_up ;;
  down) cmd_down ;;
  smoke) cmd_smoke ;;
  status) cmd_status ;;
  *)
    echo "usage: $0 {up|down|smoke|status}" >&2
    exit 1
    ;;
esac
