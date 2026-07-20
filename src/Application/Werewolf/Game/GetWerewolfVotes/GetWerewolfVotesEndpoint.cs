using Application.Werewolf.Domain;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Werewolf.Game.GetWerewolfVotes;

public record WerewolfVotesResponse
{
    public required Dictionary<Guid, Guid?> Votes { get; init; }
    public required bool Locked { get; init; }
    public required Guid? LockedTarget { get; init; }
}

// Werewolf pack coordination (who's voting for whom, whether the target has locked) is deliberately
// pulled over plain HTTP rather than pushed via SignalR (see Notifications/PlayerNotification.cs) --
// this endpoint checks the caller is themselves a living werewolf before returning anything, and
// returns 404 (not 403) for anyone else so the response itself never confirms or denies pack
// membership. Confirmed design decision, not a TODO -- see GAME_FLOW.md §7's "Confirmed design
// decision" note for why a private per-player SignalR push (technically fine, see seer.result/
// night.turn/hunter.turn) still loses to keeping this one auditable poll-and-404 boundary.
public static class GetWerewolfVotesEndpoint
{
    [WolverineGet("/api/v1/game/{roomCode}/werewolf/votes")]
    public static async Task<WerewolfVotesResponse?> Handle(
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

        if (!state.IsAlive(playerId) || !state.Players.TryGetValue(playerId, out var player) || player.Role != Role.Werewolf)
        {
            return null;
        }

        return new WerewolfVotesResponse
        {
            Votes = new Dictionary<Guid, Guid?>(state.CurrentNight.WerewolfVotes),
            Locked = state.CurrentNight.WerewolfLocked,
            LockedTarget = state.CurrentNight.WerewolfLockedTarget
        };
    }
}
