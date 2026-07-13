using Application.Werewolf.Domain;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Application.Werewolf.Game.SubmitWerewolfVote;

public record SubmitWerewolfVote
{
    public required RoomCode RoomCode { get; init; }
    public required Guid PlayerId { get; init; }
    public required Guid TargetPlayerId { get; init; }
}

[MartenStore(typeof(IWerewolfStore))]
public static class SubmitWerewolfVoteHandler
{
    public static ProblemDetails Validate(SubmitWerewolfVote command, [ReadAggregate("RoomCode")] GameState state, CancellationToken cancellationToken)
    {
        foreach (var error in GameCommandSupport.ValidatePhase(state, GamePhase.Night))
        {
            return new ProblemDetails { Title = error };
        }

        if (!state.IsAlive(command.PlayerId) || state.Players[command.PlayerId].Role != Role.Werewolf)
        {
            return new ProblemDetails { Title = "Only living werewolves can vote." };
        }

        if (!state.IsAlive(command.TargetPlayerId) || state.Players[command.TargetPlayerId].Role == Role.Werewolf)
        {
            return new ProblemDetails { Title = "Werewolves must target a living non-werewolf." };
        }

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/v1/game/werewolf/vote")]
    public static Events Handle(SubmitWerewolfVote command, [WriteAggregate("RoomCode")] GameState state)
    {
        var events = new Events { new WerewolfVoteCast { WolfPlayerId = command.PlayerId, TargetPlayerId = command.TargetPlayerId } };

        var wolves = NightChecklist.AlivePlayersWithRole(state, Role.Werewolf).ToList();
        var votes = new Dictionary<Guid, Guid>(state.CurrentNight.WerewolfVotes) { [command.PlayerId] = command.TargetPlayerId };

        if (wolves.All(votes.ContainsKey))
        {
            if (state.Settings.WerewolfRequiresConsensus)
            {
                var distinctTargets = votes.Values.Distinct().ToList();
                if (distinctTargets.Count == 1)
                {
                    events += new WerewolfTargetLocked { TargetPlayerId = distinctTargets[0] };
                }
            }
            else
            {
                var grouped = votes.Values.GroupBy(x => x).OrderByDescending(x => x.Count()).First();
                events += new WerewolfTargetLocked { TargetPlayerId = grouped.Key };
            }
        }

        // TODO(wiring): once all night roles are done, this should also trigger night resolution
        // (GameCommandSupport.TryResolveNight) — deferred for now.
        return events;
    }
}
