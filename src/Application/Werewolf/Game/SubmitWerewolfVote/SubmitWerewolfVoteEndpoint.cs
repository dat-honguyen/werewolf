using Application.Werewolf.Domain;
using Microsoft.AspNetCore.Http;
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
    public Guid? TargetPlayerId { get; init; }
}

public static class SubmitWerewolfVoteEndpoint
{
    public static ProblemDetails Validate(SubmitWerewolfVote command, [ReadAggregate("RoomCode")] GameState state, CancellationToken cancellationToken)
    {
        foreach (var error in GameCommandSupport.ValidatePhase(state, GamePhase.Night))
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = error };
        }

        if (!state.IsAlive(command.PlayerId) || state.Players[command.PlayerId].Role != Role.Werewolf)
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Only living werewolves can vote." };
        }

        foreach (var error in GameCommandSupport.ValidateNightTurn(state, NightRoleStep.Werewolves))
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = error };
        }

        if (command.TargetPlayerId is null)
        {
            if (!state.Settings.WerewolfCanVoteNoKill)
            {
                return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Werewolves must vote for a kill target (no-kill voting is disabled for this game)." };
            }

            return WolverineContinue.NoProblems;
        }

        if (command.TargetPlayerId == command.PlayerId)
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "A werewolf cannot vote to kill themselves." };
        }

        if (!state.IsAlive(command.TargetPlayerId.Value))
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Werewolves must target a living player." };
        }

        if (state.Players[command.TargetPlayerId.Value].Role == Role.Werewolf && !state.Settings.WerewolfCanTargetWerewolf)
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Werewolves cannot target another werewolf (friendly fire is disabled for this game)." };
        }

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/v1/game/werewolf/vote")]
    public static Events Handle(SubmitWerewolfVote command, [WriteAggregate("RoomCode")] GameState state)
    {
        var events = new Events { new WerewolfVoteCast { GameId = state.Id, WolfPlayerId = command.PlayerId, TargetPlayerId = command.TargetPlayerId } };

        var wolves = NightChecklist.AlivePlayersWithRole(state, Role.Werewolf).ToList();
        var votes = new Dictionary<Guid, Guid?>(state.CurrentNight.WerewolfVotes) { [command.PlayerId] = command.TargetPlayerId };

        if (wolves.All(votes.ContainsKey))
        {
            if (state.Settings.WerewolfRequiresConsensus)
            {
                var distinctTargets = votes.Values.Distinct().ToList();
                if (distinctTargets.Count == 1)
                {
                    events += new WerewolfTargetLocked { GameId = state.Id, TargetPlayerId = distinctTargets[0] };
                }
            }
            else
            {
                var grouped = votes.Values.GroupBy(x => x).OrderByDescending(x => x.Count()).First();
                events += new WerewolfTargetLocked { GameId = state.Id, TargetPlayerId = grouped.Key };
            }
        }

        return events;
    }
}
