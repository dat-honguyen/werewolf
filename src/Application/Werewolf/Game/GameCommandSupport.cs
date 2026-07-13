using Application.Werewolf.Domain;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Werewolf.Game;

public static class GameCommandSupport
{
    internal static async Task<IEventStream<GameState>> LoadGameStream(
        IDocumentSession session,
        RoomCode roomCode,
        CancellationToken cancellationToken)
    {
        var stream = await session.Events.FetchForWriting<GameState, RoomCode>(roomCode, cancellationToken);

        if (stream.Aggregate is null)
        {
            throw new InvalidOperationException($"Game for room '{roomCode.Value}' does not exist.");
        }

        return stream;
    }

    internal static IEnumerable<string> ValidatePhase(GameState state, GamePhase phase)
    {
        if (state.Phase != phase)
        {
            yield return $"Game phase is {state.Phase}, expected {phase}.";
        }
    }

    internal static IEnumerable<string> ValidateHost(GameState state, Guid playerId)
    {
        if (state.HostPlayerId != playerId)
        {
            yield return "Only the host can perform this action.";
        }
    }

    internal static IEnumerable<string> ValidateHunterRevengeTurn(GameState state, Guid playerId)
    {
        if (state.PendingHunterRevenge.Count == 0 || state.PendingHunterRevenge.Peek() != playerId)
        {
            yield return "It is not this player's hunter revenge turn.";
        }
    }

    internal static bool AllAliveVoted(GameState state) =>
        state.AlivePlayers().All(id => state.CurrentVote.Votes.ContainsKey(id));

    /// <summary>
    /// If the night checklist is complete, resolves the night death cascade (and any phase
    /// transition it unblocks) and returns the events to append. Returns an empty <see cref="Events"/>
    /// otherwise, so this is safe to call speculatively — e.g. from <see cref="GameFlowTriggerHandler"/>
    /// every time a night-role action might have just completed the checklist.
    /// </summary>
    internal static Events TryResolveNight(GameState state)
    {
        var events = new Events();

        if (!NightChecklist.IsComplete(state))
        {
            return events;
        }

        var victims = new List<Guid>();
        var wolfTarget = state.CurrentNight.WerewolfLockedTarget;
        if (wolfTarget.HasValue)
        {
            var saved = state.CurrentNight.WitchUsedHeal || state.CurrentNight.DoctorProtectedTarget == wolfTarget;
            if (!saved)
            {
                victims.Add(wolfTarget.Value);
            }
        }

        if (state.CurrentNight.WitchPoisonTarget.HasValue)
        {
            victims.Add(state.CurrentNight.WitchPoisonTarget.Value);
        }

        var resolution = DeathResolver.Resolve(state, victims);

        events += new NightResolved
        {
            NightDeaths = resolution.DeadPlayers.ToList()
        };

        foreach (var playerId in resolution.DeadPlayers)
        {
            events += new PlayerDied { PlayerId = playerId, Cause = "night" };
        }

        foreach (var hunterId in resolution.PendingHunterRevenge)
        {
            events += new HunterRevengePending { HunterPlayerId = hunterId };
        }

        events.AddRange(TryResumeAfterHunterResolution(state, GamePhase.Night, resolution.DeadPlayers, resolution.PendingHunterRevenge.Count));

        return events;
    }

    /// <summary>
    /// Closes voting, determines the lynch target (or no-lynch), resolves the resulting death
    /// cascade, and returns the events to append. Callers are responsible for guarding phase — this
    /// always closes voting unconditionally, matching both the explicit host <c>CloseVoting</c>
    /// action and the auto-close-when-all-voted trigger.
    /// </summary>
    internal static Events CloseVotingAndResolve(GameState state)
    {
        var events = new Events { new VotingClosed { ClosedAtUtc = DateTime.UtcNow } };

        var voted = state.CurrentVote.Votes.Where(x => x.Value.HasValue)
            .GroupBy(x => x.Value!.Value)
            .Select(x => new { PlayerId = x.Key, Count = x.Count() })
            .OrderByDescending(x => x.Count)
            .ToList();

        if (voted.Count == 0 || (voted.Count > 1 && voted[0].Count == voted[1].Count))
        {
            events += new NoLynchOccurred();
            events += new NightStarted { NightNumber = state.NightNumber + 1, StartedAtUtc = DateTime.UtcNow };
            return events;
        }

        var lynchTarget = voted[0].PlayerId;
        events += new LynchTargetDetermined { TargetPlayerId = lynchTarget };
        events += new PlayerLynched { PlayerId = lynchTarget };
        events += new PlayerDied { PlayerId = lynchTarget, Cause = "lynch" };

        var deaths = DeathResolver.Resolve(state, [lynchTarget]);
        foreach (var linked in deaths.DeadPlayers.Where(x => x != lynchTarget))
        {
            events += new PlayerDied { PlayerId = linked, Cause = "lover-link" };
        }

        foreach (var hunterId in deaths.PendingHunterRevenge)
        {
            events += new HunterRevengePending { HunterPlayerId = hunterId };
        }

        events.AddRange(TryResumeAfterHunterResolution(state, GamePhase.DayResolution, deaths.DeadPlayers, deaths.PendingHunterRevenge.Count));

        return events;
    }

    /// <summary>
    /// Fires the Hunter's revenge shot: appends the shot itself, resolves the death cascade for the
    /// target (including any lover-link chain), and resumes whichever phase transition the death
    /// that queued this Hunter had paused.
    /// </summary>
    internal static Events ResolveHunterRevengeShot(GameState state, Guid hunterPlayerId, Guid targetPlayerId)
    {
        var events = new Events
        {
            new HunterRevengeShotFired { HunterPlayerId = hunterPlayerId, TargetPlayerId = targetPlayerId }
        };

        var deaths = DeathResolver.Resolve(state, [targetPlayerId]);
        foreach (var playerId in deaths.DeadPlayers)
        {
            events += new PlayerDied
            {
                PlayerId = playerId,
                Cause = playerId == targetPlayerId ? "hunter-revenge" : "lover-link"
            };
        }

        foreach (var newHunterId in deaths.PendingHunterRevenge)
        {
            events += new HunterRevengePending { HunterPlayerId = newHunterId };
        }

        events.AddRange(TryResumeAfterHunterResolution(state, state.Phase, deaths.DeadPlayers, deaths.PendingHunterRevenge.Count, dequeuedCount: 1));

        return events;
    }

    /// <summary>
    /// Declines the Hunter's revenge shot and resumes whichever phase transition the death that
    /// queued this Hunter had paused.
    /// </summary>
    internal static Events DeclineHunterRevenge(GameState state, Guid hunterPlayerId)
    {
        var events = new Events { new HunterRevengeDeclined { HunterPlayerId = hunterPlayerId } };
        events.AddRange(TryResumeAfterHunterResolution(state, state.Phase, [], newlyPendingCount: 0, dequeuedCount: 1));
        return events;
    }

    /// <summary>
    /// Resumes the phase transition that a night-resolution, lynch, or hunter-revenge shot was in
    /// the middle of once no Hunter revenge shots remain outstanding. The pre-append
    /// <paramref name="state"/> does not yet reflect events just built alongside this call, so
    /// callers pass <paramref name="pausedPhase"/> (the phase the transition is resuming from —
    /// <paramref name="state"/>'s own <c>Phase</c> may already be stale, e.g. after building a
    /// not-yet-folded <c>VotingClosed</c>), <paramref name="newlyDead"/> (players this same
    /// resolution just killed, for win-condition evaluation), <paramref name="dequeuedCount"/>
    /// (hunters just resolved off the front of the queue), and <paramref name="newlyPendingCount"/>
    /// (new HunterRevengePending events just built).
    /// </summary>
    internal static Events TryResumeAfterHunterResolution(
        GameState state,
        GamePhase pausedPhase,
        IReadOnlyCollection<Guid> newlyDead,
        int newlyPendingCount,
        int dequeuedCount = 0)
    {
        var events = new Events();

        if (state.PendingHunterRevenge.Count - dequeuedCount + newlyPendingCount > 0)
        {
            return events;
        }

        var winner = WinConditionEvaluator.Evaluate(state, newlyDead);
        if (winner.HasValue)
        {
            events += new GameEnded { WinningFaction = winner.Value, EndedAtUtc = DateTime.UtcNow };
            return events;
        }

        if (pausedPhase == GamePhase.Night)
        {
            events += new DayStarted { DayNumber = state.DayNumber + 1, StartedAtUtc = DateTime.UtcNow };
        }
        else if (pausedPhase == GamePhase.DayResolution)
        {
            events += new NightStarted { NightNumber = state.NightNumber + 1, StartedAtUtc = DateTime.UtcNow };
        }

        return events;
    }
}
