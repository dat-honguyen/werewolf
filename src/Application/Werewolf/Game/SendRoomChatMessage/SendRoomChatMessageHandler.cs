using Application.Werewolf.Domain;
using Application.Werewolf.Lobby;
using System;
using Wolverine.SignalR;

namespace Application.Werewolf.Game.SendRoomChatMessage;

/// <summary>
/// Sent by a client over its already-established SignalR connection (see JoinGameRoom's docs for
/// the envelope shape) -- no HTTP endpoint for this one, matching JoinGameRoom/LeaveGameRoom rather
/// than the request/response game-action commands.
/// </summary>
public record SendRoomChatMessage : WebSocketMessage
{
    public required RoomCode RoomCode { get; init; }
    public required Guid PlayerId { get; init; }
    public required string Text { get; init; }
}

/// <summary>
/// Appends to LobbyState (not GameState) so Town Square works from the moment a room is created --
/// through an active game, and across rematches -- rather than only once a GameState exists. See
/// RoomChatMessageSent's docs for why.
/// </summary>
public static class SendRoomChatMessageHandler
{
    public const int MaxMessageLength = 500;

    /// <summary>
    /// No HTTP response to relay a rejection to on this transport (unlike the old POST endpoint's
    /// ProblemDetails), so invalid sends are silently dropped -- same posture as
    /// GameFlowTriggerHandler's TryResolveNight/TryCloseVoting no-ops. The client already guards
    /// empty/oversized text before it ever calls the hub.
    /// </summary>
    public static Events Handle(SendRoomChatMessage command, [WriteAggregate("RoomCode")] LobbyState state)
    {
        var text = command.Text.Trim();
        if (!state.Players.ContainsKey(command.PlayerId) || text.Length == 0 || text.Length > MaxMessageLength)
        {
            return [];
        }

        return
        [
            new RoomChatMessageSent
            {
                LobbyId = state.Id,
                SenderId = command.PlayerId,
                Text = text,
                SentAtUtc = DateTime.UtcNow
            }
        ];
    }
}
