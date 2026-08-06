using Application.Werewolf.Domain;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Werewolf.Game.GetSeerStatus;

public record SeerInspectionResult
{
    public required Guid TargetPlayerId { get; init; }
    public required bool IsWerewolf { get; init; }
}

public record SeerStatusResponse
{
    // Lets a client that just (re)loaded reconstruct whether the Seer panel should show as
    // already acted, rather than relying on local component state that resets on every page
    // load (the seer.result SignalR push only ever fires once, at the moment she inspects).
    public required bool HasActedThisNight { get; init; }
    public required SeerInspectionResult? LastInspection { get; init; }
}

public static class GetSeerStatusEndpoint
{
    [WolverineGet("/api/v1/game/{roomCode}/seer/status")]
    public static async Task<SeerStatusResponse?> Handle(
        RoomCode roomCode,
        Guid playerId,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var state = await session.Events.FetchLatest<GameState, RoomCode>(roomCode, cancellationToken);
        if (state is null)
        {
            return null;
        }

        if (!state.IsAlive(playerId) || !state.Players.TryGetValue(playerId, out var player) || player.Role != Role.Seer)
        {
            return null;
        }

        SeerInspectionResult? lastInspection = null;
        if (state.CurrentNight.SeerInspections.TryGetValue(playerId, out var targetPlayerId))
        {
            lastInspection = new SeerInspectionResult
            {
                TargetPlayerId = targetPlayerId,
                IsWerewolf = state.Players[targetPlayerId].Role == Role.Werewolf
            };
        }

        return new SeerStatusResponse
        {
            HasActedThisNight = state.CurrentNight.SeerDone,
            LastInspection = lastInspection
        };
    }
}
