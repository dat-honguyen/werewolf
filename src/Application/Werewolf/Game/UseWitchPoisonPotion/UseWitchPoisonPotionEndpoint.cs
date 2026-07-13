using Application.Werewolf.Domain;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;

namespace Application.Werewolf.Game.UseWitchPoisonPotion;

public record UseWitchPoisonPotion
{
    public required RoomCode RoomCode { get; init; }
    public required Guid PlayerId { get; init; }
    public required Guid TargetPlayerId { get; init; }
}

public static class UseWitchPoisonPotionEndpoint
{
    public static ProblemDetails Validate(UseWitchPoisonPotion command, [ReadAggregate("RoomCode")] GameState state, CancellationToken cancellationToken)
    {
        foreach (var error in GameCommandSupport.ValidatePhase(state, GamePhase.Night))
        {
            return new ProblemDetails { Title = error };
        }

        if (!state.IsAlive(command.PlayerId) || state.Players[command.PlayerId].Role != Role.Witch || state.CurrentNight.WitchDone)
        {
            return new ProblemDetails { Title = "Witch action is not available." };
        }

        if (state.Players[command.PlayerId].WitchPoisonPotionUsed)
        {
            return new ProblemDetails { Title = "The poison potion has already been used." };
        }

        if (!state.IsAlive(command.TargetPlayerId))
        {
            return new ProblemDetails { Title = "Poison target must be alive." };
        }

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/v1/game/witch/poison")]
    public static Events Handle(UseWitchPoisonPotion command, [WriteAggregate("RoomCode")] GameState state) =>
        [new WitchPoisonUsed { WitchPlayerId = command.PlayerId, TargetPlayerId = command.TargetPlayerId }];
}
