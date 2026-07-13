using Application.Werewolf.Domain;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;

namespace Application.Werewolf.Game.SubmitHunterRevengeShot;

public record SubmitHunterRevengeShot
{
    public required RoomCode RoomCode { get; init; }
    public required Guid PlayerId { get; init; }
    public required Guid TargetPlayerId { get; init; }
}

public static class SubmitHunterRevengeShotEndpoint
{
    public static ProblemDetails Validate(SubmitHunterRevengeShot command, [ReadAggregate("RoomCode")] GameState state, CancellationToken cancellationToken)
    {
        foreach (var error in GameCommandSupport.ValidateHunterRevengeTurn(state, command.PlayerId))
        {
            return new ProblemDetails { Title = error };
        }

        if (!state.IsAlive(command.TargetPlayerId))
        {
            return new ProblemDetails { Title = "Hunter revenge target must be alive." };
        }

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/v1/game/hunter/shoot")]
    public static Events Handle(SubmitHunterRevengeShot command, [WriteAggregate("RoomCode")] GameState state) =>
        GameCommandSupport.ResolveHunterRevengeShot(state, command.PlayerId, command.TargetPlayerId);
}
