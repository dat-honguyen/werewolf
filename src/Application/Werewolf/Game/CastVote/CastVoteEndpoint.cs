using Application.Werewolf.Domain;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;

namespace Application.Werewolf.Game.CastVote;

public record CastVote
{
    public required RoomCode RoomCode { get; init; }
    public required Guid VoterPlayerId { get; init; }
    public Guid? TargetPlayerId { get; init; }
}

public static class CastVoteEndpoint
{
    public static ProblemDetails Validate(CastVote command, [ReadAggregate("RoomCode")] GameState state, CancellationToken cancellationToken)
    {
        foreach (var error in GameCommandSupport.ValidatePhase(state, GamePhase.DayVoting))
        {
            return new ProblemDetails { Title = error };
        }

        if (!state.IsAlive(command.VoterPlayerId))
        {
            return new ProblemDetails { Title = "Only living players can vote." };
        }

        if (command.TargetPlayerId.HasValue && !state.IsAlive(command.TargetPlayerId.Value))
        {
            return new ProblemDetails { Title = "Vote target must be alive." };
        }

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/v1/game/vote")]
    public static Events Handle(CastVote command, [WriteAggregate("RoomCode")] GameState state)
    {
        // TODO(wiring): once all alive players have voted, this should also close voting and
        // resolve the lynch (GameCommandSupport.CloseVotingAndResolve) — deferred for now.
        return [new VoteCast { VoterPlayerId = command.VoterPlayerId, TargetPlayerId = command.TargetPlayerId }];
    }
}
