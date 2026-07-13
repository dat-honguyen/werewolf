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

[MartenStore(typeof(IWerewolfStore))]
public static class CloseVotingHandler
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
    public static Events Handle(CloseVoting command, [WriteAggregate("RoomCode")] GameState state)
    {
        // TODO(wiring): this should also determine the lynch target (or NoLynchOccurred) and
        // resolve the death cascade (GameCommandSupport.CloseVotingAndResolve) — deferred for now.
        return [new VotingClosed { ClosedAtUtc = DateTime.UtcNow }];
    }
}
