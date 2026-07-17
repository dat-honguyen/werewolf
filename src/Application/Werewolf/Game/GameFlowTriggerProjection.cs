using Application.Werewolf.Domain;
using Marten.Events.Aggregation;
using System;
using System.Threading.Tasks;

namespace Application.Werewolf.Game;

/// <summary>
/// Minimal persisted doc that exists only so <see cref="GameFlowTriggerProjection"/> has a slice to
/// run <c>RaiseSideEffects</c> against — it deliberately does not duplicate <see cref="GameState"/>.
/// </summary>
public record GameFlowTrigger
{
    public required Guid Id { get; init; }
    public required RoomCode RoomCode { get; init; }
}

/// <summary>
/// Watches the Game event stream and fires the internal <see cref="TryResolveNight"/> /
/// <see cref="TryCloseVoting"/> commands whenever an independent actor's action might have just
/// completed a system-wide checklist (all night roles done, all alive players voted). The actual
/// completeness re-check happens in <see cref="GameFlowTriggerHandler"/> against freshly-loaded
/// state, so publishing these unconditionally here is safe.
/// </summary>
public partial class GameFlowTriggerProjection : SingleStreamProjection<GameFlowTrigger, Guid>
{
    public const int VERSION = 1;

    public GameFlowTriggerProjection()
    {
        Version = VERSION;
    }

    public static GameFlowTrigger Create(IEvent<GameStarted> @event) =>
        new() { Id = @event.Data.GameId, RoomCode = @event.Data.RoomCode };

    // No-op Apply overloads: SingleStreamProjection only includes event types in a slice if it has
    // an Apply/Create method for them, so RaiseSideEffects below would never see these events
    // otherwise — the doc itself doesn't need any data from them.
    public static GameFlowTrigger Apply(IEvent<WerewolfTargetLocked> @event, GameFlowTrigger trigger) => trigger;
    public static GameFlowTrigger Apply(IEvent<DoctorProtectionChosen> @event, GameFlowTrigger trigger) => trigger;
    public static GameFlowTrigger Apply(IEvent<SeerInspectionPerformed> @event, GameFlowTrigger trigger) => trigger;
    public static GameFlowTrigger Apply(IEvent<WitchHealUsed> @event, GameFlowTrigger trigger) => trigger;
    public static GameFlowTrigger Apply(IEvent<WitchPoisonUsed> @event, GameFlowTrigger trigger) => trigger;
    public static GameFlowTrigger Apply(IEvent<WitchPassed> @event, GameFlowTrigger trigger) => trigger;
    public static GameFlowTrigger Apply(IEvent<CupidPairedLovers> @event, GameFlowTrigger trigger) => trigger;
    public static GameFlowTrigger Apply(IEvent<VoteCast> @event, GameFlowTrigger trigger) => trigger;
    public static GameFlowTrigger Apply(IEvent<PlayerDied> @event, GameFlowTrigger trigger) => trigger;

    // NightResolved/VotingClosed themselves need no side effect (nothing to re-check -- they *are*
    // the resolution), but including them in the slice lets RaiseSideEffects below tell "PlayerDied
    // because TryResolveNight/CloseVotingAndResolve just settled the night/vote in this same batch"
    // apart from "PlayerDied because a quit needs a fresh checklist re-check": the former always
    // appends its own NightResolved/VotingClosed earlier in the same event list, so republishing
    // TryResolveNight/TryCloseVoting for those deaths would just re-load the aggregate and no-op
    // against the CurrentNight.Resolved/Phase guard already set by this same batch.
    public static GameFlowTrigger Apply(IEvent<NightResolved> @event, GameFlowTrigger trigger) => trigger;
    public static GameFlowTrigger Apply(IEvent<VotingClosed> @event, GameFlowTrigger trigger) => trigger;

    public override ValueTask RaiseSideEffects(IDocumentOperations ops, IEventSlice<GameFlowTrigger> slice)
    {
        var trigger = slice.Snapshot;
        if (trigger is null)
        {
            return ValueTask.CompletedTask;
        }

        var resolvedInThisBatch = false;

        foreach (var e in slice.Events())
        {
            switch (e.Data)
            {
                case WerewolfTargetLocked:
                case DoctorProtectionChosen:
                case SeerInspectionPerformed:
                case WitchHealUsed:
                case WitchPoisonUsed:
                case WitchPassed:
                case CupidPairedLovers:
                    slice.PublishMessage(new TryResolveNight { RoomCode = trigger.RoomCode });
                    break;

                case VoteCast:
                    slice.PublishMessage(new TryCloseVoting { RoomCode = trigger.RoomCode });
                    break;

                case NightResolved:
                case VotingClosed:
                    // Always appended before the PlayerDied events it causes (see
                    // GameCommandSupport.TryResolveNight/CloseVotingAndResolve), so this flips before
                    // the case below sees those deaths.
                    resolvedInThisBatch = true;
                    break;

                case PlayerDied when resolvedInThisBatch:
                    // This death came from the same TryResolveNight/TryCloseVoting call that already
                    // settled the night/vote in this batch -- republishing would just reload the
                    // aggregate and no-op. A death from an independent cause (a quit) never has
                    // NightResolved/VotingClosed earlier in its own batch, so it still falls through
                    // to the case below.
                    break;

                case PlayerDied:
                    // A quit (or any other death) can complete whichever checklist is currently
                    // blocking -- e.g. the last living Doctor quitting mid-Night, or the last
                    // undecided voter quitting mid-DayVoting. Both handlers re-check their own guard
                    // (phase + checklist) against fresh state, so firing both unconditionally here is
                    // a safe no-op when neither applies.
                    slice.PublishMessage(new TryResolveNight { RoomCode = trigger.RoomCode });
                    slice.PublishMessage(new TryCloseVoting { RoomCode = trigger.RoomCode });
                    break;
            }
        }

        return ValueTask.CompletedTask;
    }
}
