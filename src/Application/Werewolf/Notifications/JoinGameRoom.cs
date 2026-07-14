using Application.Werewolf.Domain;
using System;
using Wolverine;
using Wolverine.SignalR;

namespace Application.Werewolf.Notifications;

/// <summary>
/// Sent by a client over its already-established SignalR connection to subscribe to a room's
/// notifications (and, if it identifies as a specific player, that player's private notifications).
/// </summary>
public record JoinGameRoom : WebSocketMessage
{
    public required RoomCode RoomCode { get; init; }
    public Guid? PlayerId { get; init; }
}

public static class JoinGameRoomHandler
{
    // ISideEffect return values must be statically typed (a bare value or tuple slot) — Wolverine
    // only recognizes them via each handler-call "Creates" variable's declared type, so boxing them
    // into IEnumerable<object> hides them from the side-effect codegen policy and instead routes
    // them through the ordinary cascading-message pipeline, which rejects ISideEffect outright.
    public static (AddConnectionToGroup, AddConnectionToGroup?) Handle(JoinGameRoom command) =>
        (
            new AddConnectionToGroup(PlayerNotification.RoomGroup(command.RoomCode)),
            command.PlayerId.HasValue
                ? new AddConnectionToGroup(PlayerNotification.PlayerGroup(command.RoomCode, command.PlayerId.Value))
                : null
        );
}

public record LeaveGameRoom : WebSocketMessage
{
    public required RoomCode RoomCode { get; init; }
    public Guid? PlayerId { get; init; }
}

public static class LeaveGameRoomHandler
{
    public static (RemoveConnectionToGroup, RemoveConnectionToGroup?) Handle(LeaveGameRoom command) =>
        (
            new RemoveConnectionToGroup(PlayerNotification.RoomGroup(command.RoomCode)),
            command.PlayerId.HasValue
                ? new RemoveConnectionToGroup(PlayerNotification.PlayerGroup(command.RoomCode, command.PlayerId.Value))
                : null
        );
}
