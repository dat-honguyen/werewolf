using Application.Werewolf.Domain;
using Application.Werewolf.Game;
using System;
using System.Collections.Generic;
using System.Linq;
using Wolverine;
using Wolverine.SignalR;

namespace Application.Werewolf.Notifications;

public record PlayerNotification : IGroupWebsocketMessage
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

public static class GameEventToNotificationHandler
{
    public static SignalRMessage<PlayerNotification> Handle(GameStarted @event) =>
        PlayerNotification.Broadcast(@event.RoomCode, "game.started", new { @event.GameId }).ToWebSocketDestination();

    public static SignalRMessage<PlayerNotification> Handle(PlayerDied @event, [ReadAggregate("GameId")] GameState state) =>
        PlayerNotification.Broadcast(state.RoomCode, "player.died", new
        {
            @event.PlayerId,
            @event.Cause,
            Role = state.Settings.RevealRoleOnDeath ? state.Players[@event.PlayerId].Role : (Role?)null
        }).ToWebSocketDestination();

    public static SignalRMessage<PlayerNotification> Handle(PlayerLynched @event, [ReadAggregate("GameId")] GameState state) =>
        PlayerNotification.Broadcast(state.RoomCode, "player.lynched", new
        {
            @event.PlayerId,
            Role = state.Settings.RevealRoleOnDeath ? state.Players[@event.PlayerId].Role : (Role?)null
        }).ToWebSocketDestination();

    public static SignalRMessage<PlayerNotification> Handle(SeerInspectionPerformed @event, [ReadAggregate("GameId")] GameState state) =>
        PlayerNotification.ToPlayer(
            state.RoomCode,
            @event.SeerPlayerId,
            "seer.result",
            new { @event.TargetPlayerId, @event.IsWerewolf }).ToWebSocketDestination();

    // Werewolf pack coordination (who's voting for whom, whether the target has locked) is
    // deliberately NOT pushed over SignalR: which player IDs are in the room's SignalR player-group
    // fan-out is observable, and a live werewolf-only channel is one more surface that could leak
    // pack membership. Living werewolves instead poll GetWerewolfVotesEndpoint
    // (GET /api/v1/game/{roomCode}/werewolf/votes) over plain authenticated HTTP, which checks the
    // caller is themselves a living werewolf before returning anything.

    public static SignalRMessage<PlayerNotification> Handle(VoteCast @event, [ReadAggregate("GameId")] GameState state) =>
        PlayerNotification.Broadcast(state.RoomCode, "vote.cast", new { @event.VoterPlayerId, @event.TargetPlayerId }).ToWebSocketDestination();

    public static SignalRMessage<PlayerNotification> Handle(DayStarted @event, [ReadAggregate("GameId")] GameState state) =>
        PlayerNotification.Broadcast(state.RoomCode, "day.started", new { @event.DayNumber }).ToWebSocketDestination();

    public static IEnumerable<object> Handle(NightStarted @event, [ReadAggregate("GameId")] GameState state) =>
        new object[] { PlayerNotification.Broadcast(state.RoomCode, "night.started", new { @event.NightNumber }).ToWebSocketDestination() }
            .Concat(NightTurnNotifications(state));

    /// <summary>
    /// The Werewolves' turn ends the moment their target locks (see <see cref="NightRoleStep"/>) —
    /// this is where the narrator hands the night off to whichever role comes next (Doctor, per the
    /// fixed order), rather than revealing the lock itself to non-wolves.
    /// </summary>
    public static IEnumerable<object> Handle(WerewolfTargetLocked @event, [ReadAggregate("GameId")] GameState state) =>
        NightTurnNotifications(state);

    public static IEnumerable<object> Handle(DoctorProtectionChosen @event, [ReadAggregate("GameId")] GameState state) =>
        NightTurnNotifications(state);

    public static IEnumerable<object> Handle(CupidPairedLovers @event, [ReadAggregate("GameId")] GameState state) =>
        NightTurnNotifications(state);

    public static IEnumerable<object> Handle(WitchHealUsed @event, [ReadAggregate("GameId")] GameState state) =>
        NightTurnNotifications(state);

    public static IEnumerable<object> Handle(WitchPoisonUsed @event, [ReadAggregate("GameId")] GameState state) =>
        NightTurnNotifications(state);

    public static IEnumerable<object> Handle(WitchPassed @event, [ReadAggregate("GameId")] GameState state) =>
        NightTurnNotifications(state);

    /// <summary>
    /// Narrates whichever night role is up next per <see cref="NightRoleStep"/>: a room-wide
    /// broadcast with flavor text that never names a player (so nobody's role leaks from the
    /// broadcast alone), plus a private "it's your turn" push to whichever living player(s) actually
    /// hold that role. Returns nothing once the checklist is complete (the death cascade narrates
    /// itself via the usual PlayerDied/NightResolved events).
    /// </summary>
    private static IEnumerable<object> NightTurnNotifications(GameState state)
    {
        var step = NightChecklist.CurrentStep(state);
        var role = NightNarrator.RoleFor(step);
        if (role is null)
        {
            yield break;
        }

        yield return PlayerNotification.Broadcast(state.RoomCode, "night.narration", new { Step = step, Text = NightNarrator.Prompt(step) }).ToWebSocketDestination();

        foreach (var playerId in NightChecklist.AlivePlayersWithRole(state, role.Value))
        {
            yield return PlayerNotification.ToPlayer(state.RoomCode, playerId, "night.turn", new { Role = role.Value, Prompt = NightNarrator.Prompt(step) }).ToWebSocketDestination();
        }
    }

    public static SignalRMessage<PlayerNotification> Handle(VotingStarted @event, [ReadAggregate("GameId")] GameState state) =>
        PlayerNotification.Broadcast(state.RoomCode, "voting.started").ToWebSocketDestination();

    public static SignalRMessage<PlayerNotification> Handle(GameEnded @event, [ReadAggregate("GameId")] GameState state) =>
        PlayerNotification.Broadcast(state.RoomCode, "game.ended", new
        {
            @event.WinningFaction,
            Roles = state.Players.ToDictionary(x => x.Key, x => x.Value.Role)
        }).ToWebSocketDestination();
}

/// <summary>
/// Published by RoomLobbyViewProjection.RaiseSideEffects. A projection's slice.PublishMessage
/// goes through normal message routing rather than the cascading-message pipeline, so it can't
/// return a SignalRMessage&lt;T&gt; (an ISendMyself) directly — routing finds no subscriber for that
/// wrapper type. This lightweight command bridges the two: it's routed and handled locally like
/// any other message, and RoomGroupNotificationHandler's return value goes through the cascading
/// pipeline, which does special-case ISendMyself. See docs/signalr-projection-example.md.
///
/// Deliberately NOT an IGroupWebsocketMessage: once a message type matches an explicit
/// Publish(...).ToSignalR() rule, Wolverine routes it there exclusively and skips local handler
/// dispatch — confirmed by testing against a live instance (RoomGroupNotificationHandler never
/// ran, and the raw command itself was pushed to clients, when this implemented that interface).
/// Leaving it a plain message keeps it local-only so the handler always fires.
/// </summary>
public record NotifyRoomUpdated(RoomCode RoomCode);

public static class RoomGroupNotificationHandler
{
    public static SignalRMessage<PlayerNotification> Handle(NotifyRoomUpdated message) =>
        PlayerNotification.Broadcast(message.RoomCode, "lobby.updated").ToWebSocketDestination();
}
