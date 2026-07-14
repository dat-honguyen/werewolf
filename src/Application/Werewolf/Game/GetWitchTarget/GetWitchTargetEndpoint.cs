using Application.Werewolf.Domain;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Werewolf.Game.GetWitchTarget;

public record WitchTargetResponse
{
    public required Guid? TargetPlayerId { get; init; }
}

// Gated by GameSettings.WitchKnowsWerewolfTarget: when the setting is on (the classic tabletop
// rule), the Witch can ask who the werewolves locked onto before deciding whether to heal/poison/
// pass. When it's off, she has to decide blind, so this always returns a null target rather than
// a 404/error -- a disabled setting isn't the same thing as "you're not the Witch."
public static class GetWitchTargetEndpoint
{
    [WolverineGet("/api/v1/game/{roomCode}/witch/target")]
    public static async Task<WitchTargetResponse?> Handle(
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

        if (!state.IsAlive(playerId) || !state.Players.TryGetValue(playerId, out var player) || player.Role != Role.Witch)
        {
            return null;
        }

        return new WitchTargetResponse
        {
            TargetPlayerId = state.Settings.WitchKnowsWerewolfTarget ? state.CurrentNight.WerewolfLockedTarget : null
        };
    }
}
