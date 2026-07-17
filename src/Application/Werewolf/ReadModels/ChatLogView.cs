using Application.Werewolf.Game;
using Marten.Events.Aggregation;
using System;
using System.Collections.Generic;

namespace Application.Werewolf.ReadModels;

public record ChatMessageEntry
{
    public required Guid SenderId { get; init; }
    public required string Text { get; init; }
    public required DateTime SentAtUtc { get; init; }
}

public record ChatLogView
{
    public required Guid Id { get; init; }
    public required List<ChatMessageEntry> RoomMessages { get; init; }
    public required List<ChatMessageEntry> PackMessages { get; init; }
}

/// <summary>
/// Raw append-only chat history for a game, keyed by GameId -- same Inline/cheap-list-append
/// justification as GameLogViewProjection (no DB round-trips, just an in-memory list append per
/// event). Display names are resolved at read time from PlayerDirectoryEntry, same as
/// GetGameLogEndpoint, rather than baked in here.
/// </summary>
public partial class ChatLogViewProjection : SingleStreamProjection<ChatLogView, Guid>
{
    public const int VERSION = 1;

    public ChatLogViewProjection()
    {
        Version = VERSION;
    }

    public static ChatLogView Create(IEvent<GameStarted> @event) =>
        new()
        {
            Id = @event.Data.GameId,
            RoomMessages = [],
            PackMessages = []
        };

    public static ChatLogView Apply(IEvent<RoomChatMessageSent> @event, ChatLogView view) =>
        view with
        {
            RoomMessages =
            [
                .. view.RoomMessages,
                new ChatMessageEntry
                {
                    SenderId = @event.Data.SenderId,
                    Text = @event.Data.Text,
                    SentAtUtc = @event.Data.SentAtUtc
                }
            ]
        };

    public static ChatLogView Apply(IEvent<PackChatMessageSent> @event, ChatLogView view) =>
        view with
        {
            PackMessages =
            [
                .. view.PackMessages,
                new ChatMessageEntry
                {
                    SenderId = @event.Data.SenderId,
                    Text = @event.Data.Text,
                    SentAtUtc = @event.Data.SentAtUtc
                }
            ]
        };
}
