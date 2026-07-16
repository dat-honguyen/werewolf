using Application.Werewolf.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Threading;

namespace Application.Werewolf.Game.QuitGame;

public record QuitGame
{
    public required RoomCode RoomCode { get; init; }
    public required Guid PlayerId { get; init; }
}

public static class QuitGameEndpoint
{
    public static ProblemDetails Validate(QuitGame command, [ReadAggregate("RoomCode")] GameState state, CancellationToken cancellationToken)
    {
        if (state.Phase == GamePhase.GameOver)
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "The game has already ended." };
        }

        if (!state.IsAlive(command.PlayerId))
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "This player is not an active player in this game." };
        }

        return WolverineContinue.NoProblems;
    }

    /// <summary>
    /// Marks the player dead (Cause "quit") and resolves the same death cascade a night/lynch kill
    /// would: lover-link chain death, any newly-pending Hunter revenge, and (once no Hunter revenge
    /// is left outstanding) an immediate win-condition check so the game ends right away if the quit
    /// decided it, regardless of what phase it happened in. A blocked night/vote checklist that the
    /// quit unblocks (e.g. the last living Doctor quitting) is left to <see cref="GameFlowTriggerProjection"/>'s
    /// PlayerDied trigger to resolve normally, rather than forced here.
    /// </summary>
    [WolverinePost("/api/v1/game/quit")]
    public static Events Handle(QuitGame command, [WriteAggregate("RoomCode")] GameState state)
    {
        var events = new Events { new PlayerDied { GameId = state.Id, PlayerId = command.PlayerId, Cause = "quit" } };

        var wasPendingHunter = state.PendingHunterRevenge.Count > 0 && state.PendingHunterRevenge.Peek() == command.PlayerId;
        if (wasPendingHunter)
        {
            events += new HunterRevengeDeclined { GameId = state.Id, HunterPlayerId = command.PlayerId };
        }

        var resolution = DeathResolver.Resolve(state, [command.PlayerId]);
        foreach (var linked in resolution.DeadPlayers.Where(x => x != command.PlayerId))
        {
            events += new PlayerDied { GameId = state.Id, PlayerId = linked, Cause = "lover-link" };
        }

        // A player who just quit can't be asked to pick a revenge target -- if quitting makes them
        // Hunter-eligible (they die holding an unused shot), auto-decline it in this same batch
        // instead of leaving/showing them a prompt for someone who already left. A chain death from
        // someone else (e.g. a lover-link partner) still gets a normal pending prompt -- they're
        // still around to answer it.
        var otherPendingHunters = resolution.PendingHunterRevenge.Where(x => x != command.PlayerId).ToList();
        foreach (var hunterId in otherPendingHunters)
        {
            events += new HunterRevengePending { GameId = state.Id, HunterPlayerId = hunterId };
        }
        if (resolution.PendingHunterRevenge.Contains(command.PlayerId))
        {
            events += new HunterRevengePending { GameId = state.Id, HunterPlayerId = command.PlayerId };
            events += new HunterRevengeDeclined { GameId = state.Id, HunterPlayerId = command.PlayerId };
        }

        if (wasPendingHunter)
        {
            // This quit resolves the pending shot the same way PassHunterRevengeEndpoint's decline
            // does, so it's safe to resume whatever phase transition it had paused.
            events.AddRange(GameCommandSupport.TryResumeAfterHunterResolution(
                state, state.Phase, resolution.DeadPlayers, otherPendingHunters.Count, dequeuedCount: 1));
        }
        else if (state.PendingHunterRevenge.Count + otherPendingHunters.Count == 0)
        {
            // No Hunter revenge pause in play, so it's safe to check the win condition immediately --
            // a quit can end the game right away regardless of phase (e.g. the last living werewolf
            // quits mid-discussion).
            var winner = WinConditionEvaluator.Evaluate(state, resolution.DeadPlayers);
            if (winner.HasValue)
            {
                events += new GameEnded { GameId = state.Id, WinningFaction = winner.Value, EndedAtUtc = DateTime.UtcNow };
            }
        }

        return events;
    }
}
