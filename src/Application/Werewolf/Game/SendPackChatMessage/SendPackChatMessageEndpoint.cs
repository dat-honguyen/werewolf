using Application.Werewolf.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;

namespace Application.Werewolf.Game.SendPackChatMessage;

public record SendPackChatMessage
{
    public required RoomCode RoomCode { get; init; }
    public required Guid PlayerId { get; init; }
    public required string Text { get; init; }
}

/// <summary>
/// Werewolf-pack-only chat. Never pushed over SignalR (see PackChatMessageSent) -- living
/// werewolves poll GetPackChatEndpoint instead, same pull-not-push posture as
/// GetWerewolfVotesEndpoint.
/// </summary>
public static class SendPackChatMessageEndpoint
{
    public const int MaxMessageLength = 500;

    public static ProblemDetails Validate(SendPackChatMessage command, [ReadAggregate("RoomCode")] GameState state)
    {
        if (!state.IsAlive(command.PlayerId) || !state.Players.TryGetValue(command.PlayerId, out var player) || player.Role != Role.Werewolf)
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Only living werewolves can use pack chat." };
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

    [WolverinePost("/api/v1/game/chat/pack")]
    public static Events Handle(SendPackChatMessage command, [WriteAggregate("RoomCode")] GameState state) =>
        [
            new PackChatMessageSent
            {
                GameId = state.Id,
                SenderId = command.PlayerId,
                Text = command.Text.Trim(),
                SentAtUtc = DateTime.UtcNow
            }
        ];
}
