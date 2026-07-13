using Application.Werewolf.Domain;
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

[MartenStore(typeof(IWerewolfStore))]
public static class SubmitCupidPairingHandler
{
    public static ProblemDetails Validate(SubmitCupidPairing command, [ReadAggregate("RoomCode")] GameState state, CancellationToken cancellationToken)
    {
        foreach (var error in GameCommandSupport.ValidatePhase(state, GamePhase.Night))
        {
            return new ProblemDetails { Title = error };
        }

        if (state.NightNumber != 1)
        {
            return new ProblemDetails { Title = "Cupid can only pair lovers on the first night." };
        }

        if (!state.IsAlive(command.PlayerId) || state.Players[command.PlayerId].Role != Role.Cupid || state.CurrentNight.CupidDone)
        {
            return new ProblemDetails { Title = "Cupid action is not available." };
        }

        if (command.FirstPlayerId == command.SecondPlayerId
            || !state.IsAlive(command.FirstPlayerId)
            || !state.IsAlive(command.SecondPlayerId))
        {
            return new ProblemDetails { Title = "Cupid must pair two distinct living players." };
        }

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/v1/game/cupid")]
    public static Events Handle(SubmitCupidPairing command, [WriteAggregate("RoomCode")] GameState state)
    {
        // TODO(wiring): once all night roles are done, this should also trigger night resolution
        // (GameCommandSupport.TryResolveNight) — deferred for now.
        return [new CupidPairedLovers { CupidPlayerId = command.PlayerId, FirstPlayerId = command.FirstPlayerId, SecondPlayerId = command.SecondPlayerId }];
    }
}
