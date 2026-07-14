using Application.Werewolf.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Threading;

namespace Application.Werewolf.Lobby.LeaveLobby;

public record LeaveLobby
{
    public required RoomCode RoomCode { get; init; }
    public required Guid PlayerId { get; init; }
}

public static class LeaveLobbyEndpoint
{
    public static ProblemDetails Validate(LeaveLobby command, [ReadAggregate("RoomCode")] LobbyState state, CancellationToken cancellationToken)
    {
        foreach (var error in LobbyCommandSupport.ValidateOpen(state))
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = error };
        }

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/v1/lobby/leave")]
    public static Events Handle(LeaveLobby command, [WriteAggregate("RoomCode")] LobbyState state)
    {
        if (!state.Players.ContainsKey(command.PlayerId))
        {
            return [];
        }

        var events = new Events { new PlayerLeftLobby { PlayerId = command.PlayerId, LeftAtUtc = DateTime.UtcNow } };

        if (state.HostPlayerId == command.PlayerId && state.Players.Keys.Any(x => x != command.PlayerId))
        {
            var nextHost = state.Players.Keys.First(x => x != command.PlayerId);
            events += new HostTransferred
            {
                PreviousHostPlayerId = command.PlayerId,
                NewHostPlayerId = nextHost,
                TransferredAtUtc = DateTime.UtcNow
            };
        }

        return events;
    }
}
