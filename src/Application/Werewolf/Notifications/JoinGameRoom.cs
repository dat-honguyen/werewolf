using Application.Werewolf.Domain;
using System;
using System.Collections.Generic;
using Wolverine.SignalR;

namespace Application.Werewolf.Notifications;

/// <summary>
/// Sent by a client over its already-established SignalR connection to subscribe to a room's
/// notifications (and, if it identifies as a specific player, that player's private notifications).
/// </summary>
public record JoinGameRoom
{
    public required RoomCode RoomCode { get; init; }
    public Guid? PlayerId { get; init; }
}

public static class JoinGameRoomHandler
{
    public static IEnumerable<object> Handle(JoinGameRoom command)
    {
        yield return new AddConnectionToGroup(PlayerNotification.RoomGroup(command.RoomCode));

        if (command.PlayerId.HasValue)
        {
            yield return new AddConnectionToGroup(PlayerNotification.PlayerGroup(command.RoomCode, command.PlayerId.Value));
        }
    }
}

public record LeaveGameRoom
{
    public required RoomCode RoomCode { get; init; }
    public Guid? PlayerId { get; init; }
}

public static class LeaveGameRoomHandler
{
    public static IEnumerable<object> Handle(LeaveGameRoom command)
    {
        yield return new RemoveConnectionToGroup(PlayerNotification.RoomGroup(command.RoomCode));

        if (command.PlayerId.HasValue)
        {
            yield return new RemoveConnectionToGroup(PlayerNotification.PlayerGroup(command.RoomCode, command.PlayerId.Value));
        }
    }
}
