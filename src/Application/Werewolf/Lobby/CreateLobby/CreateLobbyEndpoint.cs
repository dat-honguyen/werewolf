using Application.Werewolf.Domain;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Werewolf.Lobby.CreateLobby;

public record CreateLobby
{
    public required Guid HostPlayerId { get; init; }
    public required string HostDisplayName { get; init; }
}

public record CreateLobbyResponse
{
    public required string RoomCode { get; init; }
}

public static class CreateLobbyEndpoint
{
    [WolverinePost("/api/v1/lobby")]
    public static async Task<(CreateLobbyResponse, IStartStream)> Handle(
        CreateLobby command,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < LobbyCommandSupport.MaxRoomCodeAttempts; attempt++)
        {
            var roomCode = RoomCode.Generate(Random.Shared);
            var exists = await session.Query<LobbyState>()
                .AnyAsync(x => x.RoomCode.Value == roomCode.Value, cancellationToken);

            if (exists)
            {
                continue;
            }

            var lobbyId = Guid.NewGuid();
            var created = new LobbyCreated
            {
                LobbyId = lobbyId,
                RoomCode = roomCode,
                HostPlayerId = command.HostPlayerId,
                HostDisplayName = command.HostDisplayName.Trim(),
                CreatedAtUtc = DateTime.UtcNow
            };

            return (new() { RoomCode = roomCode.Value }, MartenOps.StartStream<LobbyState>(lobbyId, created));
        }

        throw new InvalidOperationException("Could not allocate a unique room code.");
    }
}
