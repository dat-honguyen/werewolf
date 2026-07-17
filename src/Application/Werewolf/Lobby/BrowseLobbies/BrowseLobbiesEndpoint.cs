using Application.Werewolf.Domain;
using Application.Werewolf.ReadModels;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Werewolf.Lobby.BrowseLobbies;

public record OpenLobbySummary
{
    public required string RoomCode { get; init; }
    public required string HostDisplayName { get; init; }
    public required int PlayerCount { get; init; }
    public required int MinPlayers { get; init; }
    public required List<Role> SpecialRoles { get; init; }
}

public static class BrowseLobbiesEndpoint
{
    /// <summary>
    /// Every lobby is public (no password) for now, so this simply lists every lobby still in
    /// <see cref="LobbyStatus.Open"/>, sourced from <see cref="RoomLobbyView"/> -- the only
    /// projection that stores lobby documents queryable across rooms (LobbyState itself is a Live
    /// aggregation, rebuilt per-stream, so it can't be listed in bulk).
    /// </summary>
    [WolverineGet("/api/v1/lobby/open")]
    public static async Task<List<OpenLobbySummary>> Handle(
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var lobbies = await session.Query<RoomLobbyView>()
            .Where(x => x.Status == LobbyStatus.Open)
            .ToListAsync(cancellationToken);

        return lobbies
            .OrderByDescending(x => x.Players.Count)
            .Select(x => new OpenLobbySummary
            {
                RoomCode = x.RoomCode.Value,
                HostDisplayName = x.Players.TryGetValue(x.HostPlayerId, out var host)
                    ? host.DisplayName
                    : "Unknown",
                PlayerCount = x.Players.Count,
                MinPlayers = x.Settings.MinPlayers,
                SpecialRoles = x.RoleDistribution
                    .Where(r => r.Value > 0 && r.Key != Role.Villager && r.Key != Role.Werewolf)
                    .Select(r => r.Key)
                    .OrderBy(r => r)
                    .ToList()
            })
            .ToList();
    }
}
