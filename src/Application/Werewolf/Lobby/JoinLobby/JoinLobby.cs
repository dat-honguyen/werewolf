using Application.Werewolf.Domain;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;

namespace Application.Werewolf.Lobby.JoinLobby;

public record JoinLobby
{
    public required RoomCode RoomCode { get; init; }
    public required Guid PlayerId { get; init; }
    public required string DisplayName { get; init; }
}

[MartenStore(typeof(IWerewolfStore))]
public static class JoinLobbyHandler
{
    public static ProblemDetails Validate(JoinLobby command, [ReadAggregate("RoomCode")] LobbyState state, CancellationToken cancellationToken)
    {
        foreach (var error in LobbyCommandSupport.ValidateOpen(state))
        {
            return new ProblemDetails { Title = error };
        }

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/v1/lobby/join")]
    public static Events Handle(JoinLobby command, [WriteAggregate("RoomCode")] LobbyState state)
    {
        if (state.Players.ContainsKey(command.PlayerId))
        {
            return [];
        }

        return [new PlayerJoinedLobby { PlayerId = command.PlayerId, DisplayName = command.DisplayName.Trim(), JoinedAtUtc = DateTime.UtcNow }];
    }
}
