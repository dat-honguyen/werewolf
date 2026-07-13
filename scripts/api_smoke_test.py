#!/usr/bin/env python3
"""
Quick manual API smoke test for the Werewolf backend.

Exercises: create lobby -> join 5 more players -> ready everyone up
-> start the game. Stdlib only (urllib), no pip install needed.

Usage:
    python3 scripts/api_smoke_test.py [base_url]

Defaults to http://localhost:5080. Bring the app up first, e.g.:
    .claude/skills/run-werewolf/driver.sh up
"""

import json
import sys
import urllib.error
import urllib.request
import uuid

BASE_URL = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5080"


def call(method: str, path: str, body: dict | None = None) -> tuple[int, object]:
    url = f"{BASE_URL}{path}"
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method,
                                  headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req) as resp:
            raw = resp.read()
            return resp.status, (json.loads(raw) if raw else None)
    except urllib.error.HTTPError as e:
        raw = e.read()
        try:
            return e.code, json.loads(raw)
        except json.JSONDecodeError:
            return e.code, raw.decode(errors="replace")


def step(label: str, method: str, path: str, body: dict | None, expect_status):
    status, payload = call(method, path, body)
    ok = status in expect_status if isinstance(expect_status, (list, tuple)) else status == expect_status
    marker = "OK " if ok else "FAIL"
    print(f"[{marker}] {label}: {method} {path} -> {status} {payload if payload is not None else ''}")
    if not ok:
        print(f"  expected {expect_status}, got {status}")
        sys.exit(1)
    return payload


def main():
    host_id = str(uuid.uuid4())
    resp = step("create lobby", "POST", "/api/v1/lobby", {
        "hostPlayerId": host_id,
        "hostDisplayName": "Host",
    }, 200)
    room_code = resp["roomCode"]
    print(f"  room code: {room_code}")

    # Need >= MinPlayers (5) plus the host to satisfy the default role
    # distribution (6 named roles) without tripping the "more roles than
    # players" guard -- 6 players total keeps it simple.
    player_ids = [str(uuid.uuid4()) for _ in range(5)]
    for i, pid in enumerate(player_ids, start=2):
        step(f"join player {i}", "POST", "/api/v1/lobby/join", {
            "roomCode": room_code,
            "playerId": pid,
            "displayName": f"Player{i}",
        }, 204)

    for pid in [host_id] + player_ids:
        step("set ready", "POST", "/api/v1/lobby/ready", {
            "roomCode": room_code,
            "playerId": pid,
            "isReady": True,
        }, 204)

    resp = step("start game", "POST", "/api/v1/lobby/start", {
        "roomCode": room_code,
        "requestedBy": host_id,
        "forceStart": False,
    }, 200)
    print(f"  game id: {resp['gameId']}")

    print("\nAll steps passed.")


if __name__ == "__main__":
    main()
