using Application.Werewolf.Domain;
using Microsoft.AspNetCore.Http;
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
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = error };
        }

        if (!state.IsAlive(command.PlayerId) || state.Players[command.PlayerId].Role != Role.Witch || state.CurrentNight.WitchDone)
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Witch action is not available." };
        }

        foreach (var error in GameCommandSupport.ValidateNightTurn(state, NightRoleStep.Witch))
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = error };
        }

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/v1/game/witch/pass")]
    public static Events Handle(PassWitch command, [WriteAggregate("RoomCode")] GameState state) =>
        [new WitchPassed { GameId = state.Id, WitchPlayerId = command.PlayerId }];
}
