using Application.Werewolf.Domain;
using Application.Werewolf.ReadModels;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Werewolf.Game.GetGameLog;

public record GameLogResponse
{
    public required string RoomCode { get; init; }
    public required Guid GameId { get; init; }
    public required List<GameLogEntry> Entries { get; init; }
}

// Backs the frontend's Game Over "View full game log" screen. Entries are structured
// (type + raw data) rather than pre-formatted English sentences specifically so the frontend can
// render them in the active locale -- player names, role/faction names, and death causes are all
// resolved client-side from ids/enum values, never baked into a string here.
public static class GetGameLogEndpoint
{
    [WolverineGet("/api/v1/game/{roomCode}/log")]
    public static async Task<GameLogResponse?> Handle(
        RoomCode roomCode,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var state = await session.Events.FetchLatest<GameState, RoomCode>(roomCode, cancellationToken);
        if (state is null)
        {
            return null;
        }

        var log = await session.LoadAsync<GameLogView>(state.Id, cancellationToken);
        if (log is null)
        {
            return null;
        }

        return new GameLogResponse
        {
            RoomCode = state.RoomCode.Value,
            GameId = state.Id,
            Entries = log.Entries
        };
    }
}
