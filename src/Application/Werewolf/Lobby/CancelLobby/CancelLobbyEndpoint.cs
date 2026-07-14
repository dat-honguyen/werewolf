using Application.Werewolf.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;

namespace Application.Werewolf.Lobby.CancelLobby;

public record CancelLobby
{
    public required RoomCode RoomCode { get; init; }
    public required Guid RequestedBy { get; init; }
}

public static class CancelLobbyEndpoint
{
    public static ProblemDetails Validate(CancelLobby command, [ReadAggregate("RoomCode")] LobbyState state, CancellationToken cancellationToken)
    {
        foreach (var error in LobbyCommandSupport.ValidateOpen(state))
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = error };
        }

        foreach (var error in LobbyCommandSupport.ValidateHost(state, command.RequestedBy))
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = error };
        }

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/v1/lobby/cancel")]
    public static Events Handle(CancelLobby command, [WriteAggregate("RoomCode")] LobbyState state) =>
        [new LobbyCancelled { CancelledBy = command.RequestedBy, CancelledAtUtc = DateTime.UtcNow }];
}
