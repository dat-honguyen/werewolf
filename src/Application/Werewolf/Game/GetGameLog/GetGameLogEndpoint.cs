using Application.Werewolf.Domain;
using Application.Werewolf.ReadModels;
using Microsoft.AspNetCore.Mvc;
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

public record GetGameLogQuery([FromRoute] RoomCode RoomCode);

// Test/debugging-only read endpoint: renders GameLogView's raw entries (which embed player
// GUIDs, baked in at projection time) with player display names substituted in, using
// PlayerDirectoryProjection (Id -> DisplayName, sourced from lobby join/create events) so the
// log reads like "Alice was assigned role Werewolf" instead of a wall of GUIDs.
public static class GetGameLogEndpoint
{
    [WolverineGet("/api/v1/game/{roomCode}/log")]
    public static async Task<GameLogResponse> Handle(
        [AsParameters] GetGameLogQuery query,
        [ReadAggregate("roomCode")] GameState state,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var log = await session.LoadAsync<GameLogView>(state.Id, cancellationToken)
            ?? throw new InvalidOperationException($"No log recorded for game '{state.Id}'.");

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
