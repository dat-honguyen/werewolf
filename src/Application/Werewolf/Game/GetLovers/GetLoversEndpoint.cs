using Application.Werewolf.Domain;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Werewolf.Game.GetLovers;

public record LoversResponse
{
    public required Guid FirstPlayerId { get; init; }
    public required Guid SecondPlayerId { get; init; }
}

// Mirrors GetWerewolfVotesEndpoint's reasoning: who Cupid paired is deliberately never pushed via
// SignalR. Only the two lovers themselves can learn the pairing, and only by asking over plain HTTP
// -- everyone else (including a caller with a valid roomCode but no matching playerId) gets 404, not
// 403, so a wrong guess can't be used to fish for the answer.
public static class GetLoversEndpoint
{
    [WolverineGet("/api/v1/game/{roomCode}/lovers")]
    public static async Task<LoversResponse?> Handle(
        RoomCode roomCode,
        Guid playerId,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var state = await session.Events.FetchLatest<GameState, RoomCode>(roomCode, cancellationToken);
        if (state?.Lovers is null)
        {
            return null;
        }

        if (state.Lovers.FirstPlayerId != playerId && state.Lovers.SecondPlayerId != playerId)
        {
            return null;
        }

        return new LoversResponse
        {
            FirstPlayerId = state.Lovers.FirstPlayerId,
            SecondPlayerId = state.Lovers.SecondPlayerId
        };
    }
}
