using Application.Werewolf.Domain;
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

public static class SendRoomChatMessageEndpoint
{
    public const int MaxMessageLength = 500;

    public static ProblemDetails Validate(SendRoomChatMessage command, [ReadAggregate("RoomCode")] GameState state)
    {
        if (!state.Players.ContainsKey(command.PlayerId))
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "You are not part of this game." };
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
    public static Events Handle(SendRoomChatMessage command, [WriteAggregate("RoomCode")] GameState state) =>
        [
            new RoomChatMessageSent
            {
                GameId = state.Id,
                SenderId = command.PlayerId,
                Text = command.Text.Trim(),
                SentAtUtc = DateTime.UtcNow
            }
        ];
}
