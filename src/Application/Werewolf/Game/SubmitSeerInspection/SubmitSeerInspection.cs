using Application.Werewolf.Domain;
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

[MartenStore(typeof(IWerewolfStore))]
public static class SubmitSeerInspectionHandler
{
    public static ProblemDetails Validate(SubmitSeerInspection command, [ReadAggregate("RoomCode")] GameState state, CancellationToken cancellationToken)
    {
        foreach (var error in GameCommandSupport.ValidatePhase(state, GamePhase.Night))
        {
            return new ProblemDetails { Title = error };
        }

        if (!state.IsAlive(command.PlayerId) || state.Players[command.PlayerId].Role != Role.Seer || state.CurrentNight.SeerDone)
        {
            return new ProblemDetails { Title = "Seer action is not available." };
        }

        if (command.TargetPlayerId == command.PlayerId || !state.IsAlive(command.TargetPlayerId))
        {
            return new ProblemDetails { Title = "Seer target must be a different living player." };
        }

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/v1/game/seer/inspect")]
    public static Events Handle(SubmitSeerInspection command, [WriteAggregate("RoomCode")] GameState state)
    {
        // TODO(wiring): once all night roles are done, this should also trigger night resolution
        // (GameCommandSupport.TryResolveNight) — deferred for now.
        return
        [
            new SeerInspectionPerformed
            {
                SeerPlayerId = command.PlayerId,
                TargetPlayerId = command.TargetPlayerId,
                ObservedRole = state.Players[command.TargetPlayerId].Role
            }
        ];
    }
}
