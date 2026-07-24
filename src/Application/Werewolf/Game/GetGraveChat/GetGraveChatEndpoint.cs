using Application.Werewolf.Domain;
using Application.Werewolf.Game.GetRoomChat;
using Application.Werewolf.ReadModels;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Werewolf.Game.GetGraveChat;

// Same pull-not-push, 404-either-way posture as GetPackChatEndpoint: checks the caller is
// themselves dead before returning anything, and returns 404 (not 403) for anyone else so the
// response itself never confirms or denies who's dead.
public static class GetGraveChatEndpoint
{
    [WolverineGet("/api/v1/game/{roomCode}/chat/grave")]
    public static async Task<ChatMessagesResponse?> Handle(
        RoomCode roomCode,
        Guid playerId,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var state = await session.Events.FetchLatest<GameState, RoomCode>(roomCode, cancellationToken);
        if (state is null)
        {
            return null;
        }

        if (state.IsAlive(playerId) || !state.Players.ContainsKey(playerId))
        {
            return null;
        }

        var log = await session.LoadAsync<GraveChatLogView>(state.Id, cancellationToken);
        if (log is null)
        {
            return new ChatMessagesResponse { Messages = [] };
        }

        var directory = await session.LoadManyAsync<PlayerDirectoryEntry>(cancellationToken, state.Players.Keys.ToArray());
        var namesById = directory.ToDictionary(x => x.Id, x => x.DisplayName);

        return new ChatMessagesResponse
        {
            Messages = log.Messages
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
