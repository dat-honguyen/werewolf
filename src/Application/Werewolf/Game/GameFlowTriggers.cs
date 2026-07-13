using Application.Werewolf.Domain;

namespace Application.Werewolf.Game;

/// <summary>
/// Internal message (no HTTP route) published by <see cref="GameFlowTriggerProjection"/> whenever
/// a night-role action might have just completed the night checklist. <see cref="GameCommandSupport.TryResolveNight"/>
/// re-checks the checklist against freshly-loaded state before building any events, so firing this
/// more than once (or after the night has already resolved) is a safe no-op.
/// </summary>
public record TryResolveNight
{
    public required RoomCode RoomCode { get; init; }
}

/// <summary>
/// Internal message (no HTTP route) published by <see cref="GameFlowTriggerProjection"/> whenever a
/// vote might have just completed the day-vote checklist (every alive player has voted). The
/// handler re-checks the guard before acting, so firing this more than once is a safe no-op.
/// </summary>
public record TryCloseVoting
{
    public required RoomCode RoomCode { get; init; }
}

public static class GameFlowTriggerHandler
{
    public static Events Handle(TryResolveNight command, [WriteAggregate("RoomCode")] GameState state) =>
        GameCommandSupport.TryResolveNight(state);

    public static Events Handle(TryCloseVoting command, [WriteAggregate("RoomCode")] GameState state) =>
        state.Phase == GamePhase.DayVoting && GameCommandSupport.AllAliveVoted(state)
            ? GameCommandSupport.CloseVotingAndResolve(state)
            : new Events();
}
