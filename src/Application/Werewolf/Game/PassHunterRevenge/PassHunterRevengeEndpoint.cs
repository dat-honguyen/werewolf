using Application.Werewolf.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;

namespace Application.Werewolf.Game.PassHunterRevenge;

public record PassHunterRevenge
{
    public required RoomCode RoomCode { get; init; }
    public required Guid PlayerId { get; init; }
}

public static class PassHunterRevengeEndpoint
{
    public static ProblemDetails Validate(PassHunterRevenge command, [ReadAggregate("RoomCode")] GameState state, CancellationToken cancellationToken)
    {
        foreach (var error in GameCommandSupport.ValidateHunterRevengeTurn(state, command.PlayerId))
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = error };
        }

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/v1/game/hunter/pass")]
    public static Events Handle(PassHunterRevenge command, [WriteAggregate("RoomCode")] GameState state) =>
        GameCommandSupport.DeclineHunterRevenge(state, command.PlayerId);
}
