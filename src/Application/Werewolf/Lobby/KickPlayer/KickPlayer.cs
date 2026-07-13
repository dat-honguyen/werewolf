using Application.Werewolf.Domain;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;

namespace Application.Werewolf.Lobby.KickPlayer;

public record KickPlayer
{
    public required RoomCode RoomCode { get; init; }
    public required Guid RequestedBy { get; init; }
    public required Guid PlayerId { get; init; }
}

[MartenStore(typeof(IWerewolfStore))]
public static class KickPlayerHandler
{
    public static ProblemDetails Validate(KickPlayer command, [ReadAggregate("RoomCode")] LobbyState state, CancellationToken cancellationToken)
    {
        foreach (var error in LobbyCommandSupport.ValidateOpen(state))
        {
            return new ProblemDetails { Title = error };
        }

        foreach (var error in LobbyCommandSupport.ValidateHost(state, command.RequestedBy))
        {
            return new ProblemDetails { Title = error };
        }

        if (command.PlayerId == state.HostPlayerId)
        {
            return new ProblemDetails { Title = "Host cannot kick themselves." };
        }

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/v1/lobby/kick")]
    public static Events Handle(KickPlayer command, [WriteAggregate("RoomCode")] LobbyState state)
    {
        if (!state.Players.ContainsKey(command.PlayerId))
        {
            return [];
        }

        return [new PlayerKickedFromLobby { PlayerId = command.PlayerId, KickedBy = command.RequestedBy, KickedAtUtc = DateTime.UtcNow }];
    }
}
