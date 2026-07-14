using Application.Werewolf.Domain;
using Application.Werewolf.ReadModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Werewolf.Game.GetGameLog;

public record GameLogResponse
{
    public required string RoomCode { get; init; }
    public required Guid GameId { get; init; }
    public required List<string> Entries { get; init; }
}

// Test/debugging-only read endpoint: renders GameLogView's raw entries (which embed player
// GUIDs, baked in at projection time) with player display names substituted in, using
// PlayerDirectoryProjection (Id -> DisplayName, sourced from lobby join/create events) so the
// log reads like "Alice was assigned role Werewolf" instead of a wall of GUIDs.
//
// [ReadAggregate]/[Aggregate] only resolve identity as a Guid route value in this Wolverine
// version, which doesn't fit a RoomCode natural key — so this fetches directly and relies on
// Wolverine's nullable-return 200-or-404 convention instead (see claude.md).
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

        var directory = await session.LoadManyAsync<PlayerDirectoryEntry>(cancellationToken, state.Players.Keys.ToArray());
        var namesById = directory.ToDictionary(x => x.Id, x => x.DisplayName);

        var entries = log.Entries
            .Select(entry => namesById.Aggregate(entry, (text, pair) => text.Replace(pair.Key.ToString(), pair.Value)))
            .ToList();

        return new GameLogResponse
        {
            RoomCode = state.RoomCode.Value,
            GameId = state.Id,
            Entries = entries
        };
    }
}
