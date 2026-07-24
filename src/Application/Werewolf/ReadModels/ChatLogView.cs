using Application.Werewolf.Game;
using Application.Werewolf.Lobby;
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

/// <summary>
/// Town Square history, keyed by LobbyId -- same Inline/cheap-list-append justification as
/// GameLogView (no DB round-trips, just an in-memory list append per event). Created on
/// LobbyCreated (not GameStarted), so chat has history from the moment a room exists, through an
/// active game, and across rematches (LobbyState's stream id is stable for the room's whole
/// lifetime, unlike GameState's, which is a fresh stream every round). Display names are resolved
/// at read time from PlayerDirectoryEntry, same as GetGameLogEndpoint, rather than baked in here.
/// </summary>
public record RoomChatLogView
{
    public required Guid Id { get; init; }
    public required List<ChatMessageEntry> Messages { get; init; }
}

public partial class RoomChatLogViewProjection : SingleStreamProjection<RoomChatLogView, Guid>
{
    public const int VERSION = 1;

    public RoomChatLogViewProjection()
    {
        Version = VERSION;
    }

    public static RoomChatLogView Create(IEvent<LobbyCreated> @event) =>
        new() { Id = @event.Data.LobbyId, Messages = [] };

    public static RoomChatLogView Apply(IEvent<RoomChatMessageSent> @event, RoomChatLogView view) =>
        view with
        {
            Messages =
            [
                .. view.Messages,
                new ChatMessageEntry
                {
                    SenderId = @event.Data.SenderId,
                    Text = @event.Data.Text,
                    SentAtUtc = @event.Data.SentAtUtc
                }
            ]
        };
}

/// <summary>
/// Pack Chat history, keyed by GameId -- unlike Town Square, this only makes sense once a game is
/// underway (werewolf roles have to exist), so it stays scoped to the GameState stream and resets
/// each round like every other GameId-keyed read model.
/// </summary>
public record PackChatLogView
{
    public required Guid Id { get; init; }
    public required List<ChatMessageEntry> Messages { get; init; }
}

public partial class PackChatLogViewProjection : SingleStreamProjection<PackChatLogView, Guid>
{
    public const int VERSION = 1;

    public PackChatLogViewProjection()
    {
        Version = VERSION;
    }

    public static PackChatLogView Create(IEvent<GameStarted> @event) =>
        new() { Id = @event.Data.GameId, Messages = [] };

    public static PackChatLogView Apply(IEvent<PackChatMessageSent> @event, PackChatLogView view) =>
        view with
        {
            Messages =
            [
                .. view.Messages,
                new ChatMessageEntry
                {
                    SenderId = @event.Data.SenderId,
                    Text = @event.Data.Text,
                    SentAtUtc = @event.Data.SentAtUtc
                }
            ]
        };
}

/// <summary>
/// Grave Chat history, keyed by GameId -- same reasoning as Pack Chat: only makes sense once a
/// game is underway (someone has to have died), so it stays scoped to the GameState stream and
/// resets each round.
/// </summary>
public record GraveChatLogView
{
    public required Guid Id { get; init; }
    public required List<ChatMessageEntry> Messages { get; init; }
}

public partial class GraveChatLogViewProjection : SingleStreamProjection<GraveChatLogView, Guid>
{
    public const int VERSION = 1;

    public GraveChatLogViewProjection()
    {
        Version = VERSION;
    }

    public static GraveChatLogView Create(IEvent<GameStarted> @event) =>
        new() { Id = @event.Data.GameId, Messages = [] };

    public static GraveChatLogView Apply(IEvent<GraveChatMessageSent> @event, GraveChatLogView view) =>
        view with
        {
            Messages =
            [
                .. view.Messages,
                new ChatMessageEntry
                {
                    SenderId = @event.Data.SenderId,
                    Text = @event.Data.Text,
                    SentAtUtc = @event.Data.SentAtUtc
                }
            ]
        };
}
