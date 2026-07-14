#!/usr/bin/env python3
"""
Plays a full 8-player Werewolf game via the HTTP API, from lobby creation
through however many night/day cycles it takes to reach GameEnded, then
renders a plot of the game timeline (alive-player count per phase).

Bots act deterministically-but-simply so the game reliably terminates:
  - Werewolves always vote the first alive non-werewolf (by id order).
  - Seer/Doctor act on the first other alive player.
  - Witch heals the wolves' locked target once (single use), otherwise passes.
  - Cupid pairs the first two alive non-Cupid players on night 1.
  - Villagers vote for the first alive werewolf they can "see" is suspicious --
    since we don't have real player agency here, they vote for the first
    alive non-self player, which reliably lynches someone every day.
  - Hunter always uses their revenge shot on the first other alive player.

Usage:
    python3 scripts/play_full_game.py [base_url]

Defaults to http://localhost:5080. Bring the app up first, e.g.:
    .claude/skills/run-werewolf/driver.sh up

Requires matplotlib for the plot (pip install matplotlib); the game
simulation itself only needs the stdlib.
"""

import json
import sys
import time
import urllib.error
import urllib.request
import uuid

BASE_URL = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5080"


def call(method: str, path: str, body: dict | None = None):
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


def post(label: str, path: str, body: dict, expect=(200, 204)):
    status, payload = call("POST", path, body)
    ok = status in expect
    print(f"  [{'OK ' if ok else 'FAIL'}] {label}: {status} {payload if payload is not None else ''}")
    if not ok:
        raise RuntimeError(f"{label} failed: {status} {payload}")
    return payload


def get_state(room_code: str) -> dict:
    status, payload = call("GET", f"/api/v1/game/{room_code}")
    if status != 200:
        raise RuntimeError(f"get_state failed: {status} {payload}")
    return payload


def wait_for(room_code: str, predicate, timeout=10.0, poll=0.2):
    """Poll GET /api/v1/game/{roomCode} until predicate(state) is true (phase
    transitions are async, driven by a Wolverine-managed projection message,
    not inline in the triggering POST's response)."""
    deadline = time.time() + timeout
    state = get_state(room_code)
    while not predicate(state):
        if time.time() > deadline:
            raise TimeoutError(f"timed out waiting for state; last state: {state}")
        time.sleep(poll)
        state = get_state(room_code)
    return state


def alive_ids(state: dict, exclude: set | None = None) -> list[str]:
    exclude = exclude or set()
    return [p["playerId"] for p in state["players"] if p["isAlive"] and p["playerId"] not in exclude]


def role_of(state: dict, player_id: str) -> str:
    return next(p["role"] for p in state["players"] if p["playerId"] == player_id)


def alive_with_role(state: dict, role: str) -> list[str]:
    return [p["playerId"] for p in state["players"] if p["isAlive"] and p["role"] == role]


def main():
    timeline = []  # list of (label, alive_count, phase)
    last_doctor_target: dict[str, str] = {}

    host_id = str(uuid.uuid4())
    resp = post("create lobby", "/api/v1/lobby", {
        "hostPlayerId": host_id, "hostDisplayName": "Host",
    }, expect=(200,))
    room_code = resp["roomCode"]
    print(f"room code: {room_code}")

    player_ids = [str(uuid.uuid4()) for _ in range(7)]
    all_ids = [host_id] + player_ids
    for i, pid in enumerate(player_ids, start=2):
        post(f"join player {i}", "/api/v1/lobby/join", {
            "roomCode": room_code, "playerId": pid, "displayName": f"Player{i}",
        })

    for pid in all_ids:
        post("set ready", "/api/v1/lobby/ready", {
            "roomCode": room_code, "playerId": pid, "isReady": True,
        })

    resp = post("start game", "/api/v1/lobby/start", {
        "roomCode": room_code, "requestedBy": host_id, "forceStart": False,
    }, expect=(200,))
    print(f"game id: {resp['gameId']}")

    state = get_state(room_code)
    print(f"roles: {[(p['playerId'][:8], p['role']) for p in state['players']]}")
    timeline.append(("start", len(alive_ids(state)), "RoleAssignment"))

    max_rounds = 20  # safety cap so a stalemate config can't loop forever
    for round_num in range(1, max_rounds + 1):
        state = get_state(room_code)
        if state["phase"] == "GameOver":
            break

        assert state["phase"] == "Night", f"expected Night, got {state['phase']}"
        night_number = state["nightNumber"]
        print(f"\n=== Night {night_number} ===")

        # Cupid: night 1 only. Deliberately avoid pairing the werewolf as a
        # lover -- otherwise the lover-link death chain can accidentally kill
        # the wolf alongside whoever the village lynches turn one, ending the
        # game in a single round instead of running several nights.
        if night_number == 1:
            cupids = alive_with_role(state, "Cupid")
            if cupids:
                non_wolves = [p["playerId"] for p in state["players"]
                              if p["isAlive"] and p["role"] not in ("Cupid", "Werewolf")]
                post("cupid pairs lovers", "/api/v1/game/cupid", {
                    "roomCode": room_code, "playerId": cupids[0],
                    "firstPlayerId": non_wolves[0], "secondPlayerId": non_wolves[1],
                })

        # Seer.
        for seer in alive_with_role(state, "Seer"):
            target = alive_ids(state, exclude={seer})[0]
            post(f"seer inspects", "/api/v1/game/seer/inspect", {
                "roomCode": room_code, "playerId": seer, "targetPlayerId": target,
            })

        # Doctor: rotate targets night to night -- the same player can no
        # longer be protected on two consecutive nights.
        for doctor in alive_with_role(state, "Doctor"):
            candidates = alive_ids(state, exclude={doctor})
            last = last_doctor_target.get(doctor)
            target = next((c for c in candidates if c != last), candidates[0])
            last_doctor_target[doctor] = target
            post("doctor protects", "/api/v1/game/doctor/protect", {
                "roomCode": room_code, "playerId": doctor, "targetPlayerId": target,
            })

        # Werewolves: all vote the same first-alive non-wolf target (consensus required).
        wolves = alive_with_role(state, "Werewolf")
        non_wolves = [p["playerId"] for p in state["players"] if p["isAlive"] and p["role"] != "Werewolf"]
        if wolves and non_wolves:
            wolf_target = non_wolves[0]
            for wolf in wolves:
                post("werewolf votes", "/api/v1/game/werewolf/vote", {
                    "roomCode": room_code, "playerId": wolf, "targetPlayerId": wolf_target,
                })

        # Witch: heal the wolves' locked target once (single potion use), else pass.
        witches = alive_with_role(state, "Witch")
        if witches:
            witch = witches[0]
            witch_row = next(p for p in state["players"] if p["playerId"] == witch)
            state_now = wait_for(room_code, lambda s: s["werewolfLockedTarget"] is not None or not wolves)
            locked = state_now["werewolfLockedTarget"]
            already_healed = False  # per-game single use; we track via a 400 fallback below
            if locked:
                status, _ = call("POST", "/api/v1/game/witch/heal", {
                    "roomCode": room_code, "playerId": witch,
                })
                if status not in (200, 204):
                    post("witch passes (heal unavailable)", "/api/v1/game/witch/pass", {
                        "roomCode": room_code, "playerId": witch,
                    })
                else:
                    print(f"  [OK ] witch heals: {status}")
            else:
                post("witch passes (no lock)", "/api/v1/game/witch/pass", {
                    "roomCode": room_code, "playerId": witch,
                })

        # Night resolves automatically once the checklist completes; poll for it. A Hunter dying
        # during the night pauses the phase transition (still reports "Night") until the revenge
        # shot/pass resolves, so a non-empty pendingHunterRevenge also counts as "done waiting" here.
        state = wait_for(room_code, lambda s: s["phase"] != "Night" or s["pendingHunterRevenge"], timeout=15)

        # Resolve any pending Hunter revenge (always shoot the first other alive player).
        while state["pendingHunterRevenge"]:
            hunter = state["pendingHunterRevenge"][0]
            target_pool = alive_ids(state, exclude={hunter})
            if target_pool:
                post("hunter shoots", "/api/v1/game/hunter/shoot", {
                    "roomCode": room_code, "playerId": hunter, "targetPlayerId": target_pool[0],
                })
            else:
                post("hunter passes", "/api/v1/game/hunter/pass", {
                    "roomCode": room_code, "playerId": hunter,
                })
            state = wait_for(room_code, lambda s: s["pendingHunterRevenge"] != state["pendingHunterRevenge"] or s["phase"] == "GameOver")

        timeline.append((f"night {night_number} resolved", len(alive_ids(state)), state["phase"]))
        print(f"  phase after night: {state['phase']}, alive: {len(alive_ids(state))}")

        if state["phase"] == "GameOver":
            break

        assert state["phase"] == "DayDiscussion", f"expected DayDiscussion, got {state['phase']}"
        print(f"=== Day {state['dayNumber']} ===")
        post("advance to voting", "/api/v1/game/voting/advance", {
            "roomCode": room_code, "requestedBy": host_id,
        })
        state = wait_for(room_code, lambda s: s["phase"] == "DayVoting")

        # Everyone votes for the same rotating *non-werewolf* target. Real
        # villagers don't reliably identify the wolf either, so this script
        # deliberately never lynches the werewolf -- that keeps the game
        # running for several night/day cycles (until the werewolf's kills
        # bring alive non-wolves down to parity, a "Werewolves win" ending)
        # instead of it potentially ending in a single lucky round.
        living = alive_ids(state)
        non_wolves_alive = [p["playerId"] for p in state["players"] if p["isAlive"] and p["role"] != "Werewolf"]
        rotating_target = non_wolves_alive[round_num % len(non_wolves_alive)]
        for voter in living:
            if rotating_target != voter:
                target = rotating_target
            else:
                fallback_pool = [p for p in non_wolves_alive if p != voter] or [p for p in living if p != voter]
                target = fallback_pool[0]
            post("cast vote", "/api/v1/game/vote", {
                "roomCode": room_code, "voterPlayerId": voter, "targetPlayerId": target,
            })

        # Voting auto-closes once everyone alive has voted; poll for resolution.
        state = wait_for(room_code, lambda s: s["phase"] != "DayVoting", timeout=15)

        while state["pendingHunterRevenge"]:
            hunter = state["pendingHunterRevenge"][0]
            target_pool = alive_ids(state, exclude={hunter})
            if target_pool:
                post("hunter shoots", "/api/v1/game/hunter/shoot", {
                    "roomCode": room_code, "playerId": hunter, "targetPlayerId": target_pool[0],
                })
            else:
                post("hunter passes", "/api/v1/game/hunter/pass", {
                    "roomCode": room_code, "playerId": hunter,
                })
            state = wait_for(room_code, lambda s: s["pendingHunterRevenge"] != state["pendingHunterRevenge"] or s["phase"] == "GameOver")

        timeline.append((f"day {state['dayNumber'] if state['dayNumber'] else night_number} vote resolved", len(alive_ids(state)), state["phase"]))
        print(f"  phase after vote: {state['phase']}, alive: {len(alive_ids(state))}")
    else:
        raise RuntimeError(f"game did not end within {max_rounds} rounds")

    state = get_state(room_code)
    result = state["result"]
    print(f"\n=== GAME OVER: {result['winningFaction']} wins ===")
    print(f"final roles: {result['finalRoles']}")
    timeline.append(("game over", len(alive_ids(state)), "GameOver"))

    plot_timeline(room_code, result["winningFaction"], timeline)


def plot_timeline(room_code: str, winner: str, timeline: list[tuple[str, int, str]]):
    try:
        import matplotlib
        matplotlib.use("Agg")
        import matplotlib.pyplot as plt
    except ImportError:
        print("\n(matplotlib not installed -- skipping plot; pip install matplotlib to enable it)")
        return

    labels = [t[0] for t in timeline]
    alive = [t[1] for t in timeline]

    fig, ax = plt.subplots(figsize=(10, 5))
    ax.plot(range(len(alive)), alive, marker="o", color="firebrick")
    ax.set_xticks(range(len(labels)))
    ax.set_xticklabels(labels, rotation=45, ha="right")
    ax.set_ylabel("Players alive")
    ax.set_title(f"Werewolf game {room_code} -- winner: {winner}")
    ax.grid(True, alpha=0.3)
    fig.tight_layout()

    out_path = f"game_{room_code}_timeline.png"
    fig.savefig(out_path, dpi=120)
    print(f"\nplot saved to {out_path}")


if __name__ == "__main__":
    main()
