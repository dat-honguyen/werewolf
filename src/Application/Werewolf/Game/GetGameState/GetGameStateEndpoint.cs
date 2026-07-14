using Application.Werewolf.Domain;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Application.Werewolf.Game.GetGameState;

public record GameStateResponse
{
    public required string RoomCode { get; init; }
    public required GamePhase Phase { get; init; }
    public required int NightNumber { get; init; }
    public required int DayNumber { get; init; }
    public required List<GamePlayerDto> Players { get; init; }
    public LoversPair? Lovers { get; init; }
    public required Guid? WerewolfLockedTarget { get; init; }
    public required List<Guid> PendingHunterRevenge { get; init; }
    public GameResult? Result { get; init; }
}

public record GamePlayerDto
{
    public required Guid PlayerId { get; init; }
    public required Role Role { get; init; }
    public required bool IsAlive { get; init; }
}

public record GetGameStateQuery([FromRoute] RoomCode RoomCode);

// Test/debugging-only read endpoint: GameState is a LiveStreamAggregation (never persisted as
// a document), so there's no read model to query it from otherwise. Resolves via the same
// natural-key mechanism [ReadAggregate]/[WriteAggregate] use elsewhere, read-only (FetchLatest).
public static class GetGameStateEndpoint
{
    [WolverineGet("/api/v1/game/{roomCode}")]
    public static GameStateResponse Handle(
        [AsParameters] GetGameStateQuery query,
        [ReadAggregate("roomCode")] GameState state)
    {
        return new GameStateResponse
        {
            RoomCode = state.RoomCode.Value,
            Phase = state.Phase,
            NightNumber = state.NightNumber,
            DayNumber = state.DayNumber,
            Players = state.Players.Values
                .Select(p => new GamePlayerDto { PlayerId = p.PlayerId, Role = p.Role, IsAlive = p.IsAlive })
                .ToList(),
            Lovers = state.Lovers,
            WerewolfLockedTarget = state.CurrentNight.WerewolfLockedTarget,
            PendingHunterRevenge = state.PendingHunterRevenge.ToList(),
            Result = state.Result
        };
    }
}
