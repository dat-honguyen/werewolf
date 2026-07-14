using Application.Werewolf.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;

namespace Application.Werewolf.Game.AdvanceToVoting;

public record AdvanceToVoting
{
    public required RoomCode RoomCode { get; init; }
    public required Guid RequestedBy { get; init; }
}

public static class AdvanceToVotingEndpoint
{
    public static ProblemDetails Validate(AdvanceToVoting command, [ReadAggregate("RoomCode")] GameState state, CancellationToken cancellationToken)
    {
        foreach (var error in GameCommandSupport.ValidatePhase(state, GamePhase.DayDiscussion))
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = error };
        }

        foreach (var error in GameCommandSupport.ValidateHost(state, command.RequestedBy))
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = error };
        }

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/v1/game/voting/advance")]
    public static Events Handle(AdvanceToVoting command, [WriteAggregate("RoomCode")] GameState state) =>
        [new VotingStarted { GameId = state.Id, StartedAtUtc = DateTime.UtcNow }];
}
