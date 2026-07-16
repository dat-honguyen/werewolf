using Application.Werewolf.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Werewolf.Lobby;

internal static class LobbyCommandSupport
{
    internal const int MaxRoomCodeAttempts = 20;

    internal static IEnumerable<string> ValidateOpen(LobbyState state)
    {
        if (state.Status != LobbyStatus.Open)
        {
            yield return "Lobby is not open.";
        }
    }

    internal static IEnumerable<string> ValidateHost(LobbyState state, Guid playerId)
    {
        if (state.HostPlayerId != playerId)
        {
            yield return "Only the host can perform this action.";
        }
    }

    /// <summary>
    /// These five roles (plus Tanner) are one-of-a-kind in the classic game, and GameState only
    /// ever tracks "has the Doctor/Seer acted this night" as a single per-night flag (not keyed
    /// per-player, unlike e.g. the Werewolves' vote dictionary) -- so a distribution assigning two
    /// Doctors would let the first holder's action silently lock the second one out of the same
    /// slot for the rest of the night. Enforced here rather than reworking GameState's tracking to
    /// be per-player for a configuration no real game actually wants.
    /// </summary>
    private static readonly Role[] UniqueRoles = [Role.Doctor, Role.Seer, Role.Witch, Role.Hunter, Role.Cupid, Role.Tanner];

    internal static IEnumerable<string> ValidateRoleDistribution(Dictionary<Role, int> distribution, int playerCount)
    {
        if (distribution.Values.Any(x => x < 0))
        {
            yield return "Role distribution cannot contain negative values.";
        }

        var wolves = distribution.GetValueOrDefault(Role.Werewolf, 0);
        if (wolves < 1 || wolves * 2 >= playerCount)
        {
            yield return "Werewolf count must be at least one and less than half of players.";
        }

        foreach (var role in UniqueRoles)
        {
            if (distribution.GetValueOrDefault(role, 0) > 1)
            {
                yield return $"{role} is a unique role and can have at most one holder.";
            }
        }

        var assigned = distribution.Values.Sum();
        if (assigned > playerCount)
        {
            yield return "Role distribution assigns more roles than players.";
        }
    }

    internal static Dictionary<Guid, Role> AssignRoles(LobbyState lobby)
    {
        var allPlayers = lobby.Players.Keys.OrderBy(_ => Random.Shared.Next()).ToList();
        var roleBag = new List<Role>();

        foreach (var pair in lobby.RoleDistribution)
        {
            roleBag.AddRange(Enumerable.Repeat(pair.Key, pair.Value));
        }

        while (roleBag.Count < allPlayers.Count)
        {
            roleBag.Add(Role.Villager);
        }

        roleBag = roleBag.OrderBy(_ => Random.Shared.Next()).ToList();

        return allPlayers.Select((playerId, index) => new { playerId, role = roleBag[index] })
            .ToDictionary(x => x.playerId, x => x.role);
    }
}
