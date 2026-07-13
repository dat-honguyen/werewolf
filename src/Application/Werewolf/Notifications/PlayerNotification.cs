using Application.Werewolf.Domain;
using Application.Werewolf.Game;
using System;
using System.Linq;
using Wolverine.SignalR;

namespace Application.Werewolf.Notifications;

public record PlayerNotification
{
    public required RoomCode RoomCode { get; init; }
    public required string Kind { get; init; }
    public Guid? ToPlayerId { get; init; }
    public object? Payload { get; init; }

    /// <summary>
    /// SignalR group name every connection for a room joins on connect (see JoinRoomGroupHandler).
    /// </summary>
    public static string RoomGroup(RoomCode roomCode) => $"room:{roomCode.Value}";

    /// <summary>
    /// SignalR group name a single player's connection(s) join in addition to the room group,
    /// so role-scoped notifications (e.g. a Seer's inspection result) reach only that player.
    /// </summary>
    public static string PlayerGroup(RoomCode roomCode, Guid playerId) => $"room:{roomCode.Value}:player:{playerId:N}";

    public static PlayerNotification Broadcast(RoomCode roomCode, string kind, object? payload = null) =>
        new()
        {
            RoomCode = roomCode,
            Kind = kind,
            Payload = payload
        };

    public static PlayerNotification ToPlayer(RoomCode roomCode, Guid playerId, string kind, object? payload = null) =>
        new()
        {
            RoomCode = roomCode,
            Kind = kind,
            ToPlayerId = playerId,
            Payload = payload
        };

    public SignalRMessage<PlayerNotification> ToWebSocketDestination() =>
        ToPlayerId.HasValue
            ? this.ToWebSocketGroup(PlayerGroup(RoomCode, ToPlayerId.Value))
            : this.ToWebSocketGroup(RoomGroup(RoomCode));
}

[MartenStore(typeof(IWerewolfStore))]
public static class GameEventToNotificationHandlers
{
    public static SignalRMessage<PlayerNotification> Handle(GameStarted @event) =>
        PlayerNotification.Broadcast(@event.RoomCode, "game.started", new { @event.GameId }).ToWebSocketDestination();

    public static SignalRMessage<PlayerNotification> Handle(PlayerDied @event, [ReadAggregate] GameState state) =>
        PlayerNotification.Broadcast(state.RoomCode, "player.died", new
        {
            @event.PlayerId,
            @event.Cause,
            Role = state.Settings.RevealRoleOnDeath ? state.Players[@event.PlayerId].Role : (Role?)null
        }).ToWebSocketDestination();

    public static SignalRMessage<PlayerNotification> Handle(PlayerLynched @event, [ReadAggregate] GameState state) =>
        PlayerNotification.Broadcast(state.RoomCode, "player.lynched", new
        {
            @event.PlayerId,
            Role = state.Settings.RevealRoleOnDeath ? state.Players[@event.PlayerId].Role : (Role?)null
        }).ToWebSocketDestination();

    public static SignalRMessage<PlayerNotification> Handle(SeerInspectionPerformed @event, [ReadAggregate] GameState state) =>
        PlayerNotification.ToPlayer(
            state.RoomCode,
            @event.SeerPlayerId,
            "seer.result",
            new { @event.TargetPlayerId, @event.ObservedRole }).ToWebSocketDestination();

    public static SignalRMessage<PlayerNotification> Handle(DayStarted @event, [ReadAggregate] GameState state) =>
        PlayerNotification.Broadcast(state.RoomCode, "day.started", new { @event.DayNumber }).ToWebSocketDestination();

    public static SignalRMessage<PlayerNotification> Handle(NightStarted @event, [ReadAggregate] GameState state) =>
        PlayerNotification.Broadcast(state.RoomCode, "night.started", new { @event.NightNumber }).ToWebSocketDestination();

    public static SignalRMessage<PlayerNotification> Handle(VotingStarted @event, [ReadAggregate] GameState state) =>
        PlayerNotification.Broadcast(state.RoomCode, "voting.started").ToWebSocketDestination();

    public static SignalRMessage<PlayerNotification> Handle(GameEnded @event, [ReadAggregate] GameState state) =>
        PlayerNotification.Broadcast(state.RoomCode, "game.ended", new
        {
            @event.WinningFaction,
            Roles = state.Players.ToDictionary(x => x.Key, x => x.Value.Role)
        }).ToWebSocketDestination();
}
