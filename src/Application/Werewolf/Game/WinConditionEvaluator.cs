using Application.Werewolf.Domain;
using System.Collections.Generic;
using System.Linq;

namespace Application.Werewolf.Game;

public static class WinConditionEvaluator
{
    /// <summary>
    /// Evaluates the win condition. <paramref name="newlyDead"/> lets a caller mid-way through
    /// building a resolution (e.g. <see cref="GameCommandSupport.TryResumeAfterHunterResolution"/>)
    /// treat players it just decided to kill as dead even though those <c>PlayerDied</c> events
    /// haven't been folded into <paramref name="state"/> yet.
    /// </summary>
    public static WinningFaction? Evaluate(GameState state, IReadOnlyCollection<Guid>? newlyDead = null)
    {
        var alive = state.Players.Values
            .Where(x => x.IsAlive && (newlyDead is null || !newlyDead.Contains(x.PlayerId)))
            .ToList();

        if (alive.Count == 0)
        {
            return WinningFaction.Villagers;
        }

        if (state.Lovers is { } lovers)
        {
            var loversAlive = alive.Count(x => x.PlayerId == lovers.FirstPlayerId || x.PlayerId == lovers.SecondPlayerId);
            if (alive.Count == 2 && loversAlive == 2)
            {
                return WinningFaction.Lovers;
            }
        }

        var aliveWolves = alive.Count(x => x.Role == Role.Werewolf);
        var aliveNonWolves = alive.Count - aliveWolves;

        if (aliveWolves == 0)
        {
            return WinningFaction.Villagers;
        }

        if (aliveWolves >= aliveNonWolves)
        {
            return WinningFaction.Werewolves;
        }

        return null;
    }
}
