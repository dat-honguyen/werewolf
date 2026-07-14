using Application.Werewolf.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Werewolf.Lobby.GetLobby;

public record LobbyStateResponse
{
    public required string RoomCode { get; init; }
    public required LobbyStatus Status { get; init; }
    public required Guid HostPlayerId { get; init; }
    public required List<LobbyPlayerDto> Players { get; init; }
    public required GameSettings Settings { get; init; }
    public required Dictionary<Role, int> RoleDistribution { get; init; }
}

public record LobbyPlayerDto
{
    public required Guid PlayerId { get; init; }
    public required string DisplayName { get; init; }
    public required bool IsReady { get; init; }
}

public static class GetLobbyEndpoint
{
    [WolverineGet("/api/v1/lobby/{roomCode}")]
    public static async Task<LobbyStateResponse?> Handle(
        RoomCode roomCode,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var state = await session.Events.FetchLatest<LobbyState, RoomCode>(roomCode, cancellationToken);
        if (state is null)
        {
            return null;
        }

        return new LobbyStateResponse
        {
            RoomCode = state.RoomCode.Value,
            Status = state.Status,
            HostPlayerId = state.HostPlayerId,
            Players = state.Players.Values
                .Select(p => new LobbyPlayerDto { PlayerId = p.PlayerId, DisplayName = p.DisplayName, IsReady = p.IsReady })
                .ToList(),
            Settings = state.Settings,
            RoleDistribution = state.RoleDistribution
        };
    }
}
