using Application.Werewolf.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;

namespace Application.Werewolf.Lobby.RematchLobby;

public record RematchLobby
{
    public required RoomCode RoomCode { get; init; }
    public required Guid RequestedBy { get; init; }
}

/// <summary>
/// Reopens a closed lobby (one whose game already ended) so the same room can play another round
/// without anyone re-joining by room code. Role distribution and settings carry over unchanged from
/// the previous round; only ready flags reset (see LobbyState.Apply(LobbyReopened)). The next
/// StartGame call then creates a brand-new GameState stream (fresh GameId), so that round's chat
/// and game log start empty automatically -- both are keyed by GameId, not RoomCode.
/// </summary>
public static class RematchLobbyEndpoint
{
    public static ProblemDetails Validate(RematchLobby command, [ReadAggregate("RoomCode")] LobbyState state, CancellationToken cancellationToken)
    {
        if (state.Status != LobbyStatus.Closed)
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Lobby is not closed -- can't start a rematch." };
        }

        foreach (var error in LobbyCommandSupport.ValidateHost(state, command.RequestedBy))
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = error };
        }

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/v1/lobby/rematch")]
    public static Events Handle(RematchLobby command, [WriteAggregate("RoomCode")] LobbyState state) =>
        [new LobbyReopened { ReopenedBy = command.RequestedBy, ReopenedAtUtc = DateTime.UtcNow }];
}
