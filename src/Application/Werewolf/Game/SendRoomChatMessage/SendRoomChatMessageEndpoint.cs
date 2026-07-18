using Application.Werewolf.Domain;
using Application.Werewolf.Lobby;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;

namespace Application.Werewolf.Game.SendRoomChatMessage;

public record SendRoomChatMessage
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
public static class SendRoomChatMessageEndpoint
{
    public const int MaxMessageLength = 500;

    public static ProblemDetails Validate(SendRoomChatMessage command, [ReadAggregate("RoomCode")] LobbyState state)
    {
        if (!state.Players.ContainsKey(command.PlayerId))
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "You are not part of this room." };
        }

        if (string.IsNullOrWhiteSpace(command.Text))
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Message can't be empty." };
        }

        if (command.Text.Trim().Length > MaxMessageLength)
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = $"Message must be {MaxMessageLength} characters or fewer." };
        }

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/v1/game/chat/room")]
    public static Events Handle(SendRoomChatMessage command, [WriteAggregate("RoomCode")] LobbyState state) =>
        [
            new RoomChatMessageSent
            {
                LobbyId = state.Id,
                SenderId = command.PlayerId,
                Text = command.Text.Trim(),
                SentAtUtc = DateTime.UtcNow
            }
        ];
}
