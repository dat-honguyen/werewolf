using Application.Werewolf.Game;
using Marten.Events.Aggregation;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Application.Werewolf.ReadModels;

public record GameLogView
{
    public required Guid Id { get; init; }
    public required List<string> Entries { get; init; }
}

public partial class GameLogViewProjection : SingleStreamProjection<GameLogView, Guid>
{
    public const int VERSION = 1;

    public GameLogViewProjection()
    {
        Version = VERSION;
    }

    public static GameLogView Create(IEvent<GameStarted> @event) =>
        new()
        {
            Id = @event.Data.GameId,
            Entries = [$"Game started at {@event.Data.StartedAtUtc:O}"]
        };

    public static GameLogView Apply(IEvent<RolesAssigned> @event, GameLogView view) =>
        view with
        {
            Entries =
            [
                .. view.Entries,
                .. @event.Data.Assignments.Select(x => $"Player {x.Key} was assigned role {x.Value}")
            ]
        };

    public static GameLogView Apply(IEvent<NightStarted> @event, GameLogView view) =>
        view with { Entries = [.. view.Entries, $"Night {@event.Data.NightNumber} started"] };

    public static GameLogView Apply(IEvent<CupidPairedLovers> @event, GameLogView view) =>
        view with { Entries = [.. view.Entries, $"From {@event.Data.CupidPlayerId} (Cupid): paired {@event.Data.FirstPlayerId} and {@event.Data.SecondPlayerId} as lovers"] };

    public static GameLogView Apply(IEvent<SeerInspectionPerformed> @event, GameLogView view) =>
        view with { Entries = [.. view.Entries, $"From {@event.Data.SeerPlayerId} (Seer): inspected {@event.Data.TargetPlayerId}, saw that they {(@event.Data.IsWerewolf ? "ARE" : "are NOT")} a werewolf"] };

    public static GameLogView Apply(IEvent<DoctorProtectionChosen> @event, GameLogView view) =>
        view with { Entries = [.. view.Entries, $"From {@event.Data.DoctorPlayerId} (Doctor): protected {@event.Data.ProtectedPlayerId}"] };

    public static GameLogView Apply(IEvent<WerewolfTargetLocked> @event, GameLogView view) =>
        view with
        {
            Entries =
            [
                .. view.Entries,
                @event.Data.TargetPlayerId is { } target
                    ? $"Werewolves locked their target: {target}"
                    : "Werewolves locked in: no kill tonight"
            ]
        };

    public static GameLogView Apply(IEvent<WitchHealUsed> @event, GameLogView view) =>
        view with { Entries = [.. view.Entries, $"From {@event.Data.WitchPlayerId} (Witch): used the heal potion"] };

    public static GameLogView Apply(IEvent<WitchPoisonUsed> @event, GameLogView view) =>
        view with { Entries = [.. view.Entries, $"From {@event.Data.WitchPlayerId} (Witch): poisoned {@event.Data.TargetPlayerId}"] };

    public static GameLogView Apply(IEvent<WitchPassed> @event, GameLogView view) =>
        view with { Entries = [.. view.Entries, $"From {@event.Data.WitchPlayerId} (Witch): passed"] };

    public static GameLogView Apply(IEvent<DayStarted> @event, GameLogView view) =>
        view with { Entries = [.. view.Entries, $"Day {@event.Data.DayNumber} started"] };

    public static GameLogView Apply(IEvent<VotingStarted> _, GameLogView view) =>
        view with { Entries = [.. view.Entries, "Voting started"] };

    public static GameLogView Apply(IEvent<VoteCast> @event, GameLogView view) =>
        view with
        {
            Entries =
            [
                .. view.Entries,
                @event.Data.TargetPlayerId is { } target
                    ? $"From {@event.Data.VoterPlayerId}: voted for {target}"
                    : $"From {@event.Data.VoterPlayerId}: abstained"
            ]
        };

    public static GameLogView Apply(IEvent<VotingClosed> _, GameLogView view) =>
        view with { Entries = [.. view.Entries, "Voting closed"] };

    public static GameLogView Apply(IEvent<PlayerDied> @event, GameLogView view) =>
        view with { Entries = [.. view.Entries, $"Player {@event.Data.PlayerId} died ({@event.Data.Cause})"] };

    public static GameLogView Apply(IEvent<PlayerLynched> @event, GameLogView view) =>
        view with { Entries = [.. view.Entries, $"Player {@event.Data.PlayerId} was lynched"] };

    public static GameLogView Apply(IEvent<NoLynchOccurred> _, GameLogView view) =>
        view with { Entries = [.. view.Entries, "No one was lynched"] };

    public static GameLogView Apply(IEvent<HunterRevengePending> @event, GameLogView view) =>
        view with { Entries = [.. view.Entries, $"From {@event.Data.HunterPlayerId} (Hunter): awaiting revenge decision"] };

    public static GameLogView Apply(IEvent<HunterRevengeShotFired> @event, GameLogView view) =>
        view with { Entries = [.. view.Entries, $"From {@event.Data.HunterPlayerId} (Hunter): shot {@event.Data.TargetPlayerId} in revenge"] };

    public static GameLogView Apply(IEvent<HunterRevengeDeclined> @event, GameLogView view) =>
        view with { Entries = [.. view.Entries, $"From {@event.Data.HunterPlayerId} (Hunter): declined revenge"] };

    public static GameLogView Apply(IEvent<GameEnded> @event, GameLogView view) =>
        view with { Entries = [.. view.Entries, $"Game ended. Winner: {@event.Data.WinningFaction}"] };
}
