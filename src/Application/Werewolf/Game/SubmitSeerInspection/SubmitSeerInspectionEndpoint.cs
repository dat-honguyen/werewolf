using Application.Werewolf.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;

namespace Application.Werewolf.Game.SubmitSeerInspection;

public record SubmitSeerInspection
{
    public required RoomCode RoomCode { get; init; }
    public required Guid PlayerId { get; init; }
    public required Guid TargetPlayerId { get; init; }
}

public static class SubmitSeerInspectionEndpoint
{
    public static ProblemDetails Validate(SubmitSeerInspection command, [ReadAggregate("RoomCode")] GameState state, CancellationToken cancellationToken)
    {
        foreach (var error in GameCommandSupport.ValidatePhase(state, GamePhase.Night))
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = error };
        }

        if (!state.IsAlive(command.PlayerId) || state.Players[command.PlayerId].Role != Role.Seer || state.CurrentNight.SeerDone)
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Seer action is not available." };
        }

        if (command.TargetPlayerId == command.PlayerId || !state.IsAlive(command.TargetPlayerId))
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Seer target must be a different living player." };
        }

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/v1/game/seer/inspect")]
    public static Events Handle(SubmitSeerInspection command, [WriteAggregate("RoomCode")] GameState state)
    {
        return
        [
            new SeerInspectionPerformed
            {
                GameId = state.Id,
                SeerPlayerId = command.PlayerId,
                TargetPlayerId = command.TargetPlayerId,
                IsWerewolf = state.Players[command.TargetPlayerId].Role == Role.Werewolf
            }
        ];
    }
}
