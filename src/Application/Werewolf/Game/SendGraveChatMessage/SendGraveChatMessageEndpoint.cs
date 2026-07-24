using Application.Werewolf.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;

namespace Application.Werewolf.Game.SendGraveChatMessage;

public record SendGraveChatMessage
{
    public required RoomCode RoomCode { get; init; }
    public required Guid PlayerId { get; init; }
    public required string Text { get; init; }
}

/// <summary>
/// Dead-players-only chat. Never pushed over SignalR (see GraveChatMessageSent) -- dead players
/// poll GetGraveChatEndpoint instead, same pull-not-push posture as pack chat.
/// </summary>
public static class SendGraveChatMessageEndpoint
{
    public const int MaxMessageLength = 500;

    public static ProblemDetails Validate(SendGraveChatMessage command, [ReadAggregate("RoomCode")] GameState state)
    {
        if (state.IsAlive(command.PlayerId) || !state.Players.ContainsKey(command.PlayerId))
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Only dead players can use grave chat." };
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

    [WolverinePost("/api/v1/game/chat/grave")]
    public static Events Handle(SendGraveChatMessage command, [WriteAggregate("RoomCode")] GameState state) =>
        [
            new GraveChatMessageSent
            {
                GameId = state.Id,
                SenderId = command.PlayerId,
                Text = command.Text.Trim(),
                SentAtUtc = DateTime.UtcNow
            }
        ];
}
