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
        var game = await session.Query<GameState>()
            .SingleOrDefaultAsync(x => x.RoomCode.Value == roomCode.Value, cancellationToken);

        if (game is null)
        {
            throw new InvalidOperationException($"Game for room '{roomCode.Value}' does not exist.");
        }

        return await session.Events.FetchForWriting<GameState>(game.Id, cancellationToken);
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

    internal static void TryResolveNight(GameState state, IEventStream<GameState> stream)
    {
        if (!NightChecklist.IsComplete(state))
        {
            return;
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

        stream.AppendOne(new NightResolved
        {
            NightDeaths = resolution.DeadPlayers.ToList()
        });

        foreach (var playerId in resolution.DeadPlayers)
        {
            stream.AppendOne(new PlayerDied { PlayerId = playerId, Cause = "night" });
        }

        foreach (var hunterId in resolution.PendingHunterRevenge)
        {
            stream.AppendOne(new HunterRevengePending { HunterPlayerId = hunterId });
        }

        TryResumeAfterHunterResolution(state, stream, resolution.PendingHunterRevenge.Count);
    }

    internal static void CloseVotingAndResolve(GameState state, IEventStream<GameState> stream)
    {
        stream.AppendOne(new VotingClosed { ClosedAtUtc = DateTime.UtcNow });

        var voted = state.CurrentVote.Votes.Where(x => x.Value.HasValue)
            .GroupBy(x => x.Value!.Value)
            .Select(x => new { PlayerId = x.Key, Count = x.Count() })
            .OrderByDescending(x => x.Count)
            .ToList();

        if (voted.Count == 0 || (voted.Count > 1 && voted[0].Count == voted[1].Count))
        {
            stream.AppendOne(new NoLynchOccurred());
            stream.AppendOne(new NightStarted { NightNumber = state.NightNumber + 1, StartedAtUtc = DateTime.UtcNow });
            return;
        }

        var lynchTarget = voted[0].PlayerId;
        stream.AppendOne(new LynchTargetDetermined { TargetPlayerId = lynchTarget });
        stream.AppendOne(new PlayerLynched { PlayerId = lynchTarget });
        stream.AppendOne(new PlayerDied { PlayerId = lynchTarget, Cause = "lynch" });

        var deaths = DeathResolver.Resolve(state, [lynchTarget]);
        foreach (var linked in deaths.DeadPlayers.Where(x => x != lynchTarget))
        {
            stream.AppendOne(new PlayerDied { PlayerId = linked, Cause = "lover-link" });
        }

        foreach (var hunterId in deaths.PendingHunterRevenge)
        {
            stream.AppendOne(new HunterRevengePending { HunterPlayerId = hunterId });
        }

        TryResumeAfterHunterResolution(state, stream, deaths.PendingHunterRevenge.Count);
    }

    /// <summary>
    /// Resumes the phase transition that a night-resolution or lynch was in the middle of once no
    /// Hunter revenge shots remain outstanding. The pre-append <paramref name="state"/> does not yet
    /// reflect events just appended to the stream, so callers pass <paramref name="dequeuedCount"/>
    /// (hunters just resolved off the front of the queue) and <paramref name="newlyPendingCount"/>
    /// (new HunterRevengePending events just appended) to account for that.
    /// </summary>
    internal static void TryResumeAfterHunterResolution(
        GameState state,
        IEventStream<GameState> stream,
        int newlyPendingCount,
        int dequeuedCount = 0)
    {
        if (state.PendingHunterRevenge.Count - dequeuedCount + newlyPendingCount > 0)
        {
            return;
        }

        var winner = WinConditionEvaluator.Evaluate(state);
        if (winner.HasValue)
        {
            stream.AppendOne(new GameEnded { WinningFaction = winner.Value, EndedAtUtc = DateTime.UtcNow });
            return;
        }

        if (state.Phase == GamePhase.Night)
        {
            stream.AppendOne(new DayStarted { DayNumber = state.DayNumber + 1, StartedAtUtc = DateTime.UtcNow });
        }
        else if (state.Phase == GamePhase.DayResolution)
        {
            stream.AppendOne(new NightStarted { NightNumber = state.NightNumber + 1, StartedAtUtc = DateTime.UtcNow });
        }
    }
}
