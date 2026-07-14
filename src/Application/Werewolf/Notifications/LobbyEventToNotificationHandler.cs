using Application.Werewolf.Lobby;
using Wolverine.SignalR;

namespace Application.Werewolf.Notifications;

/// <summary>
/// Pushes a single generic "lobby.updated" notification for any Lobby-side change. Unlike Game
/// notifications, clients always re-fetch full lobby state via GET on receipt (see RoomLobbyView),
/// so no per-event payload is needed here — this just tells them something changed.
/// </summary>
public static class LobbyEventToNotificationHandler
{
    public static SignalRMessage<PlayerNotification> Handle(PlayerJoinedLobby @event, [ReadAggregate("RoomCode")] LobbyState state) =>
        PlayerNotification.Broadcast(state.RoomCode, "lobby.updated").ToWebSocketDestination();

    public static SignalRMessage<PlayerNotification> Handle(PlayerLeftLobby @event, [ReadAggregate("RoomCode")] LobbyState state) =>
        PlayerNotification.Broadcast(state.RoomCode, "lobby.updated").ToWebSocketDestination();

    public static SignalRMessage<PlayerNotification> Handle(PlayerKickedFromLobby @event, [ReadAggregate("RoomCode")] LobbyState state) =>
        PlayerNotification.Broadcast(state.RoomCode, "lobby.updated").ToWebSocketDestination();

    public static SignalRMessage<PlayerNotification> Handle(HostTransferred @event, [ReadAggregate("RoomCode")] LobbyState state) =>
        PlayerNotification.Broadcast(state.RoomCode, "lobby.updated").ToWebSocketDestination();

    public static SignalRMessage<PlayerNotification> Handle(PlayerReadyStatusChanged @event, [ReadAggregate("RoomCode")] LobbyState state) =>
        PlayerNotification.Broadcast(state.RoomCode, "lobby.updated").ToWebSocketDestination();

    public static SignalRMessage<PlayerNotification> Handle(RoleDistributionUpdated @event, [ReadAggregate("RoomCode")] LobbyState state) =>
        PlayerNotification.Broadcast(state.RoomCode, "lobby.updated").ToWebSocketDestination();

    public static SignalRMessage<PlayerNotification> Handle(GameSettingsUpdated @event, [ReadAggregate("RoomCode")] LobbyState state) =>
        PlayerNotification.Broadcast(state.RoomCode, "lobby.updated").ToWebSocketDestination();

    public static SignalRMessage<PlayerNotification> Handle(LobbyCancelled @event, [ReadAggregate("RoomCode")] LobbyState state) =>
        PlayerNotification.Broadcast(state.RoomCode, "lobby.updated").ToWebSocketDestination();

    public static SignalRMessage<PlayerNotification> Handle(LobbyClosed @event, [ReadAggregate("RoomCode")] LobbyState state) =>
        PlayerNotification.Broadcast(state.RoomCode, "lobby.updated").ToWebSocketDestination();
}
