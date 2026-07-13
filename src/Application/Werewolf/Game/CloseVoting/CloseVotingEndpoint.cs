using Application.Werewolf.Domain;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;

namespace Application.Werewolf.Game.CloseVoting;

public record CloseVoting
{
    public required RoomCode RoomCode { get; init; }
    public required Guid RequestedBy { get; init; }
}

public static class CloseVotingEndpoint
{
    public static ProblemDetails Validate(CloseVoting command, [ReadAggregate("RoomCode")] GameState state, CancellationToken cancellationToken)
    {
        foreach (var error in GameCommandSupport.ValidatePhase(state, GamePhase.DayVoting))
        {
            return new ProblemDetails { Title = error };
        }

        foreach (var error in GameCommandSupport.ValidateHost(state, command.RequestedBy))
        {
            return new ProblemDetails { Title = error };
        }

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/v1/game/voting/close")]
    public static Events Handle(CloseVoting command, [WriteAggregate("RoomCode")] GameState state) =>
        GameCommandSupport.CloseVotingAndResolve(state);
}
