using Application.Werewolf.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;

namespace Application.Werewolf.Game.UseWitchHealPotion;

public record UseWitchHealPotion
{
    public required RoomCode RoomCode { get; init; }
    public required Guid PlayerId { get; init; }
}

public static class UseWitchHealPotionEndpoint
{
    public static ProblemDetails Validate(UseWitchHealPotion command, [ReadAggregate("RoomCode")] GameState state, CancellationToken cancellationToken) 
        
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

        if (state.Players[command.PlayerId].WitchHealPotionUsed)
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "The heal potion has already been used." };
        }

        if (state.CurrentNight.WerewolfLockedTarget is null)
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "The werewolves have not locked a target yet." };
        }

        return  WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/v1/game/witch/heal")]
    public static Events Handle(UseWitchHealPotion command, [WriteAggregate("RoomCode")] GameState state)
    {

        return [new WitchHealUsed { GameId = state.Id, WitchPlayerId = command.PlayerId }];
    }
}
