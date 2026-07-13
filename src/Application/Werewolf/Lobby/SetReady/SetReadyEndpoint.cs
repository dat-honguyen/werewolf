using Application.Werewolf.Domain;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;

namespace Application.Werewolf.Lobby.SetReady;

public record SetReady
{
    public required RoomCode RoomCode { get; init; }
    public required Guid PlayerId { get; init; }
    public required bool IsReady { get; init; }
}

[MartenStore(typeof(IWerewolfStore))]
public static class SetReadyEndpoint
{
    public static ProblemDetails Validate(SetReady command, [ReadAggregate("RoomCode")] LobbyState state, CancellationToken cancellationToken)
    {
        foreach (var error in LobbyCommandSupport.ValidateOpen(state))
        {
            return new ProblemDetails { Title = error };
        }

        if (!state.Players.ContainsKey(command.PlayerId))
        {
            return new ProblemDetails { Title = "Player is not in the lobby." };
        }

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/v1/lobby/ready")]
    public static Events Handle(SetReady command, [WriteAggregate("RoomCode")] LobbyState state) =>
        [new PlayerReadyStatusChanged { PlayerId = command.PlayerId, IsReady = command.IsReady, UpdatedAtUtc = DateTime.UtcNow }];
}
