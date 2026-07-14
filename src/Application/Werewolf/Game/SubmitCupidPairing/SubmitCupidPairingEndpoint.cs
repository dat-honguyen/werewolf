using Application.Werewolf.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;

namespace Application.Werewolf.Game.SubmitCupidPairing;

public record SubmitCupidPairing
{
    public required RoomCode RoomCode { get; init; }
    public required Guid PlayerId { get; init; }
    public required Guid FirstPlayerId { get; init; }
    public required Guid SecondPlayerId { get; init; }
}

public static class SubmitCupidPairingEndpoint
{
    public static ProblemDetails Validate(SubmitCupidPairing command, [ReadAggregate("RoomCode")] GameState state, CancellationToken cancellationToken)
    {
        foreach (var error in GameCommandSupport.ValidatePhase(state, GamePhase.Night))
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = error };
        }

        if (state.NightNumber != 1)
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Cupid can only pair lovers on the first night." };
        }

        if (!state.IsAlive(command.PlayerId) || state.Players[command.PlayerId].Role != Role.Cupid || state.CurrentNight.CupidDone)
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Cupid action is not available." };
        }

        foreach (var error in GameCommandSupport.ValidateNightTurn(state, NightRoleStep.Cupid))
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = error };
        }

        if (command.FirstPlayerId == command.SecondPlayerId
            || !state.IsAlive(command.FirstPlayerId)
            || !state.IsAlive(command.SecondPlayerId))
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Cupid must pair two distinct living players." };
        }

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/v1/game/cupid")]
    public static Events Handle(SubmitCupidPairing command, [WriteAggregate("RoomCode")] GameState state) =>
        [new CupidPairedLovers { GameId = state.Id, CupidPlayerId = command.PlayerId, FirstPlayerId = command.FirstPlayerId, SecondPlayerId = command.SecondPlayerId }];
}
