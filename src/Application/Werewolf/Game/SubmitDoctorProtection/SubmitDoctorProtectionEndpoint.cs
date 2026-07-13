using Application.Werewolf.Domain;
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
            return new ProblemDetails { Title = error };
        }

        if (!state.IsAlive(command.PlayerId) || state.Players[command.PlayerId].Role != Role.Doctor || state.CurrentNight.DoctorDone)
        {
            return new ProblemDetails { Title = "Doctor action is not available." };
        }

        if (!state.IsAlive(command.TargetPlayerId))
        {
            return new ProblemDetails { Title = "Doctor target must be alive." };
        }

        if (command.TargetPlayerId == command.PlayerId && !state.Settings.DoctorCanSelfProtect)
        {
            return new ProblemDetails { Title = "Doctor cannot protect themselves." };
        }

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/v1/game/doctor/protect")]
    public static Events Handle(SubmitDoctorProtection command, [WriteAggregate("RoomCode")] GameState state) =>
        [new DoctorProtectionChosen { DoctorPlayerId = command.PlayerId, ProtectedPlayerId = command.TargetPlayerId }];
}
