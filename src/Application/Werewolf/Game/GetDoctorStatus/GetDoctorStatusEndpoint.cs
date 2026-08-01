using Application.Werewolf.Domain;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Werewolf.Game.GetDoctorStatus;

public record DoctorStatusResponse
{
    // Lets a client that just (re)loaded reconstruct whether the Doctor panel should show as
    // already acted, rather than relying on local component state that resets on every page load.
    public required bool HasActedThisNight { get; init; }
    public required Guid? ProtectedTargetPlayerId { get; init; }
}

public static class GetDoctorStatusEndpoint
{
    [WolverineGet("/api/v1/game/{roomCode}/doctor/status")]
    public static async Task<DoctorStatusResponse?> Handle(
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

        if (!state.IsAlive(playerId) || !state.Players.TryGetValue(playerId, out var player) || player.Role != Role.Doctor)
        {
            return null;
        }

        return new DoctorStatusResponse
        {
            HasActedThisNight = state.CurrentNight.DoctorDone,
            ProtectedTargetPlayerId = state.CurrentNight.DoctorProtectedTarget
        };
    }
}
