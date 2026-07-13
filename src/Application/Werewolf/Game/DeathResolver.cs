using Application.Werewolf.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Application.Werewolf.Game;

public static class DeathResolver
{
    public static DeathResolution Resolve(GameState state, IEnumerable<Guid> initialVictims)
    {
        var queue = new Queue<Guid>(initialVictims.Distinct());
        var dead = new HashSet<Guid>();
        var pendingHunters = new Queue<Guid>();

        while (queue.Count > 0)
        {
            var candidate = queue.Dequeue();

            if (!state.IsAlive(candidate) || !dead.Add(candidate))
            {
                continue;
            }

            if (state.Lovers is { } lovers)
            {
                if (candidate == lovers.FirstPlayerId && state.IsAlive(lovers.SecondPlayerId))
                {
                    queue.Enqueue(lovers.SecondPlayerId);
                }
                else if (candidate == lovers.SecondPlayerId && state.IsAlive(lovers.FirstPlayerId))
                {
                    queue.Enqueue(lovers.FirstPlayerId);
                }
            }

            var player = state.Players[candidate];
            if (player.Role == Role.Hunter && !player.HunterRevengeUsed)
            {
                pendingHunters.Enqueue(candidate);
            }
        }

        return new()
        {
            DeadPlayers = dead,
            PendingHunterRevenge = pendingHunters
        };
    }
}

public record DeathResolution
{
    public required HashSet<Guid> DeadPlayers { get; init; }
    public required Queue<Guid> PendingHunterRevenge { get; init; }
}
