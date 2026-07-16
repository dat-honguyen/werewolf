using Application.Werewolf.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
    public Role? CurrentNightRole { get; init; }
    public string? NightPrompt { get; init; }

    /// <summary>
    /// This aggregate's <see cref="GameState.Version"/> as of this read -- the "GetCurrentState()"
    /// half of the version-gap resync pattern (see <see cref="Notifications.PlayerNotification.StateVersion"/>):
    /// clients track the last version they've seen and call this endpoint whenever a SignalR
    /// notification reports a newer one, rather than polling on a timer.
    /// </summary>
    public required long Version { get; init; }
}

public record GamePlayerDto
{
    public required Guid PlayerId { get; init; }
    public required Role Role { get; init; }
    public required bool IsAlive { get; init; }
}

// Test/debugging-only read endpoint: GameState is a LiveStreamAggregation (never persisted as
// a document), so there's no read model to query it from otherwise. [ReadAggregate]/[Aggregate]
// only resolve identity as a Guid route value in this Wolverine version, which doesn't fit a
// RoomCode natural key — so this fetches directly and relies on Wolverine's nullable-return
// 200-or-404 convention instead (see claude.md).
public static class GetGameStateEndpoint
{
    [WolverineGet("/api/v1/game/{roomCode}")]
    public static async Task<GameStateResponse?> Handle(
        RoomCode roomCode,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var state = await session.Events.FetchLatest<GameState, RoomCode>(roomCode, cancellationToken);
        if (state is null)
        {
            return null;
        }

        // Exposed so clients can always resolve "whose turn is it right now" from a plain GET
        // rather than relying solely on the transient night.turn/night.narration SignalR push --
        // a client that wasn't subscribed yet when that event fired (e.g. still on the role-reveal
        // screen) would otherwise have no way to catch up.
        var nightStep = state.Phase == GamePhase.Night ? NightChecklist.CurrentStep(state) : NightRoleStep.Complete;

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
            Result = state.Result,
            CurrentNightRole = NightNarrator.RoleFor(nightStep),
            NightPrompt = nightStep == NightRoleStep.Complete ? null : NightNarrator.Prompt(nightStep),
            Version = state.Version
        };
    }
}
