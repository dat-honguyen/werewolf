using Application.Werewolf.Domain;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;

namespace Application.Werewolf.Game.UseWitchHealPotion;

public record UseWitchHealPotion
{
    public required RoomCode RoomCode { get; init; }
    public required Guid PlayerId { get; init; }
}

[MartenStore(typeof(IWerewolfStore))]
public static class UseWitchHealPotionEndpoint
{
    public static ProblemDetails Validate(UseWitchHealPotion command, [ReadAggregate("RoomCode")] GameState state, CancellationToken cancellationToken) 
        
    {

        foreach (var error in GameCommandSupport.ValidatePhase(state, GamePhase.Night))
        {
            return new ProblemDetails { Title = error };
        }

        if (!state.IsAlive(command.PlayerId) || state.Players[command.PlayerId].Role != Role.Witch || state.CurrentNight.WitchDone)
        {
            return new ProblemDetails { Title = "Witch action is not available." };
        }

        if (state.Players[command.PlayerId].WitchHealPotionUsed)
        {
            return new ProblemDetails { Title = "The heal potion has already been used." };
        }

        if (state.CurrentNight.WerewolfLockedTarget is null)
        {
            return new ProblemDetails { Title = "The werewolves have not locked a target yet." };
        }

        return  WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/v1/game/witch/heal")]
    public static Events Handle(UseWitchHealPotion command, [WriteAggregate("RoomCode")] GameState state)
    {

        return [new WitchHealUsed { WitchPlayerId = command.PlayerId }];
    }
}
