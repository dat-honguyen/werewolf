using Application.Werewolf.Domain;
using Application.Werewolf.ReadModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Werewolf.Game.GetRoomChat;

public record ChatMessageResponse
{
    public required Guid SenderId { get; init; }
    public required string SenderDisplayName { get; init; }
    public required string Text { get; init; }
    public required DateTime SentAtUtc { get; init; }
}

/// <summary>Shared response shape for both GetRoomChatEndpoint and GetPackChatEndpoint.</summary>
public record ChatMessagesResponse
{
    public required List<ChatMessageResponse> Messages { get; init; }
}

// Town Square is a public channel -- anyone who can already GET the game state can read it, same
// trust level as GetGameLogEndpoint.
public static class GetRoomChatEndpoint
{
    [WolverineGet("/api/v1/game/{roomCode}/chat/room")]
    public static async Task<ChatMessagesResponse?> Handle(
        RoomCode roomCode,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var state = await session.Events.FetchLatest<GameState, RoomCode>(roomCode, cancellationToken);
        if (state is null)
        {
            return null;
        }

        var log = await session.LoadAsync<ChatLogView>(state.Id, cancellationToken);
        if (log is null)
        {
            return new ChatMessagesResponse { Messages = [] };
        }

        var directory = await session.LoadManyAsync<PlayerDirectoryEntry>(cancellationToken, state.Players.Keys.ToArray());
        var namesById = directory.ToDictionary(x => x.Id, x => x.DisplayName);

        return new ChatMessagesResponse
        {
            Messages = log.RoomMessages
                .Select(m => new ChatMessageResponse
                {
                    SenderId = m.SenderId,
                    SenderDisplayName = namesById.GetValueOrDefault(m.SenderId, "Unknown"),
                    Text = m.Text,
                    SentAtUtc = m.SentAtUtc
                })
                .ToList()
        };
    }
}
