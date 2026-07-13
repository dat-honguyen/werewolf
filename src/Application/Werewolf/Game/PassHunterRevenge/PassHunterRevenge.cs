using Application.Werewolf.Domain;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;

namespace Application.Werewolf.Game.PassHunterRevenge;

public record PassHunterRevenge
{
    public required RoomCode RoomCode { get; init; }
    public required Guid PlayerId { get; init; }
}

[MartenStore(typeof(IWerewolfStore))]
public static class PassHunterRevengeHandler
{
    public static ProblemDetails Validate(PassHunterRevenge command, [ReadAggregate("RoomCode")] GameState state, CancellationToken cancellationToken)
    {
        foreach (var error in GameCommandSupport.ValidateHunterRevengeTurn(state, command.PlayerId))
        {
            return new ProblemDetails { Title = error };
        }

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/v1/game/hunter/pass")]
    public static Events Handle(PassHunterRevenge command, [WriteAggregate("RoomCode")] GameState state)
    {
        // TODO(wiring): this should also resume the paused phase transition
        // (GameCommandSupport.TryResumeAfterHunterResolution) — deferred for now.
        return [new HunterRevengeDeclined { HunterPlayerId = command.PlayerId }];
    }
}
