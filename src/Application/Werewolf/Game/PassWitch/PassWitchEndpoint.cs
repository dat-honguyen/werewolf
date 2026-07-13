using Application.Werewolf.Domain;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;

namespace Application.Werewolf.Game.PassWitch;

public record PassWitch
{
    public required RoomCode RoomCode { get; init; }
    public required Guid PlayerId { get; init; }
}

public static class PassWitchEndpoint
{
    public static ProblemDetails Validate(PassWitch command, [ReadAggregate("RoomCode")] GameState state, CancellationToken cancellationToken)
    {
        foreach (var error in GameCommandSupport.ValidatePhase(state, GamePhase.Night))
        {
            return new ProblemDetails { Title = error };
        }

        if (!state.IsAlive(command.PlayerId) || state.Players[command.PlayerId].Role != Role.Witch || state.CurrentNight.WitchDone)
        {
            return new ProblemDetails { Title = "Witch action is not available." };
        }

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/v1/game/witch/pass")]
    public static Events Handle(PassWitch command, [WriteAggregate("RoomCode")] GameState state)
    {
        // TODO(wiring): once all night roles are done, this should also trigger night resolution
        // (GameCommandSupport.TryResolveNight) — deferred for now.
        return [new WitchPassed { WitchPlayerId = command.PlayerId }];
    }
}
