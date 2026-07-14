using Application.Werewolf.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;

namespace Application.Werewolf.Game.SubmitDoctorProtection;

public record SubmitDoctorProtection
{
    public required RoomCode RoomCode { get; init; }
    public required Guid PlayerId { get; init; }
    public required Guid TargetPlayerId { get; init; }
}

public static class SubmitDoctorProtectionEndpoint
{
    public static ProblemDetails Validate(SubmitDoctorProtection command, [ReadAggregate("RoomCode")] GameState state, CancellationToken cancellationToken)
    {
        foreach (var error in GameCommandSupport.ValidatePhase(state, GamePhase.Night))
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = error };
        }

        if (!state.IsAlive(command.PlayerId) || state.Players[command.PlayerId].Role != Role.Doctor || state.CurrentNight.DoctorDone)
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Doctor action is not available." };
        }

        foreach (var error in GameCommandSupport.ValidateNightTurn(state, NightRoleStep.Doctor))
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = error };
        }

        if (!state.IsAlive(command.TargetPlayerId))
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Doctor target must be alive." };
        }

        if (command.TargetPlayerId == command.PlayerId && !state.Settings.DoctorCanSelfProtect)
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Doctor cannot protect themselves." };
        }

        if (command.TargetPlayerId == state.LastDoctorProtectedTarget)
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Doctor cannot protect the same player two nights in a row." };
        }

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/v1/game/doctor/protect")]
    public static Events Handle(SubmitDoctorProtection command, [WriteAggregate("RoomCode")] GameState state) =>
        [new DoctorProtectionChosen { GameId = state.Id, DoctorPlayerId = command.PlayerId, ProtectedPlayerId = command.TargetPlayerId }];
}
