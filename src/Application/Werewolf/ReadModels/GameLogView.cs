using Application.Werewolf.Game;
using Marten.Events.Aggregation;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Application.Werewolf.ReadModels;

public record GameLogEntry
{
    public required string Type { get; init; }
    public required Dictionary<string, object?> Data { get; init; }
}

public record GameLogView
{
    public required Guid Id { get; init; }
    public required List<GameLogEntry> Entries { get; init; }
}

public partial class GameLogViewProjection : SingleStreamProjection<GameLogView, Guid>
{
    public const int VERSION = 2;

    public GameLogViewProjection()
    {
        Version = VERSION;
    }

    public static GameLogView Create(IEvent<GameStarted> @event) =>
        new()
        {
            Id = @event.Data.GameId,
            Entries =
            [
                new GameLogEntry
                {
                    Type = "game_started",
                    Data = new() { ["startedAtUtc"] = @event.Data.StartedAtUtc }
                }
            ]
        };

    public static GameLogView Apply(IEvent<RolesAssigned> @event, GameLogView view) =>
        view with
        {
            Entries =
            [
                .. view.Entries,
                .. @event.Data.Assignments.Select(x => new GameLogEntry
                {
                    Type = "role_assigned",
                    Data = new() { ["playerId"] = x.Key, ["role"] = x.Value.ToString() }
                })
            ]
        };

    public static GameLogView Apply(IEvent<NightStarted> @event, GameLogView view) =>
        view with
        {
            Entries =
            [
                .. view.Entries,
                new GameLogEntry
                {
                    Type = "night_started",
                    Data = new() { ["nightNumber"] = @event.Data.NightNumber }
                }
            ]
        };

    public static GameLogView Apply(IEvent<CupidPairedLovers> @event, GameLogView view) =>
        view with
        {
            Entries =
            [
                .. view.Entries,
                new GameLogEntry
                {
                    Type = "cupid_paired_lovers",
                    Data = new()
                    {
                        ["cupidPlayerId"] = @event.Data.CupidPlayerId,
                        ["firstPlayerId"] = @event.Data.FirstPlayerId,
                        ["secondPlayerId"] = @event.Data.SecondPlayerId
                    }
                }
            ]
        };

    public static GameLogView Apply(IEvent<SeerInspectionPerformed> @event, GameLogView view) =>
        view with
        {
            Entries =
            [
                .. view.Entries,
                new GameLogEntry
                {
                    Type = "seer_inspection_performed",
                    Data = new()
                    {
                        ["seerPlayerId"] = @event.Data.SeerPlayerId,
                        ["targetPlayerId"] = @event.Data.TargetPlayerId,
                        ["isWerewolf"] = @event.Data.IsWerewolf
                    }
                }
            ]
        };

    public static GameLogView Apply(IEvent<DoctorProtectionChosen> @event, GameLogView view) =>
        view with
        {
            Entries =
            [
                .. view.Entries,
                new GameLogEntry
                {
                    Type = "doctor_protection_chosen",
                    Data = new()
                    {
                        ["doctorPlayerId"] = @event.Data.DoctorPlayerId,
                        ["protectedPlayerId"] = @event.Data.ProtectedPlayerId
                    }
                }
            ]
        };

    public static GameLogView Apply(IEvent<WerewolfTargetLocked> @event, GameLogView view) =>
        view with
        {
            Entries =
            [
                .. view.Entries,
                new GameLogEntry
                {
                    Type = @event.Data.TargetPlayerId is not null
                        ? "werewolf_target_locked"
                        : "werewolf_no_kill",
                    Data = new() { ["targetPlayerId"] = @event.Data.TargetPlayerId }
                }
            ]
        };

    public static GameLogView Apply(IEvent<WitchHealUsed> @event, GameLogView view) =>
        view with
        {
            Entries =
            [
                .. view.Entries,
                new GameLogEntry
                {
                    Type = "witch_heal_used",
                    Data = new() { ["witchPlayerId"] = @event.Data.WitchPlayerId }
                }
            ]
        };

    public static GameLogView Apply(IEvent<WitchPoisonUsed> @event, GameLogView view) =>
        view with
        {
            Entries =
            [
                .. view.Entries,
                new GameLogEntry
                {
                    Type = "witch_poison_used",
                    Data = new()
                    {
                        ["witchPlayerId"] = @event.Data.WitchPlayerId,
                        ["targetPlayerId"] = @event.Data.TargetPlayerId
                    }
                }
            ]
        };

    public static GameLogView Apply(IEvent<WitchPassed> @event, GameLogView view) =>
        view with
        {
            Entries =
            [
                .. view.Entries,
                new GameLogEntry
                {
                    Type = "witch_passed",
                    Data = new() { ["witchPlayerId"] = @event.Data.WitchPlayerId }
                }
            ]
        };

    public static GameLogView Apply(IEvent<DayStarted> @event, GameLogView view) =>
        view with
        {
            Entries =
            [
                .. view.Entries,
                new GameLogEntry
                {
                    Type = "day_started",
                    Data = new() { ["dayNumber"] = @event.Data.DayNumber }
                }
            ]
        };

    public static GameLogView Apply(IEvent<VotingStarted> _, GameLogView view) =>
        view with
        {
            Entries = [.. view.Entries, new GameLogEntry { Type = "voting_started", Data = [] }]
        };

    public static GameLogView Apply(IEvent<VoteCast> @event, GameLogView view) =>
        view with
        {
            Entries =
            [
                .. view.Entries,
                new GameLogEntry
                {
                    Type = @event.Data.TargetPlayerId is not null ? "vote_cast" : "vote_abstained",
                    Data = new()
                    {
                        ["voterPlayerId"] = @event.Data.VoterPlayerId,
                        ["targetPlayerId"] = @event.Data.TargetPlayerId
                    }
                }
            ]
        };

    public static GameLogView Apply(IEvent<VotingClosed> _, GameLogView view) =>
        view with
        {
            Entries = [.. view.Entries, new GameLogEntry { Type = "voting_closed", Data = [] }]
        };

    public static GameLogView Apply(IEvent<PlayerDied> @event, GameLogView view) =>
        view with
        {
            Entries =
            [
                .. view.Entries,
                new GameLogEntry
                {
                    Type = "player_died",
                    Data = new()
                    {
                        ["playerId"] = @event.Data.PlayerId,
                        ["cause"] = @event.Data.Cause
                    }
                }
            ]
        };

    public static GameLogView Apply(IEvent<PlayerLynched> @event, GameLogView view) =>
        view with
        {
            Entries =
            [
                .. view.Entries,
                new GameLogEntry
                {
                    Type = "player_lynched",
                    Data = new() { ["playerId"] = @event.Data.PlayerId }
                }
            ]
        };

    public static GameLogView Apply(IEvent<NoLynchOccurred> _, GameLogView view) =>
        view with
        {
            Entries = [.. view.Entries, new GameLogEntry { Type = "no_lynch_occurred", Data = [] }]
        };

    public static GameLogView Apply(IEvent<HunterRevengePending> @event, GameLogView view) =>
        view with
        {
            Entries =
            [
                .. view.Entries,
                new GameLogEntry
                {
                    Type = "hunter_revenge_pending",
                    Data = new() { ["hunterPlayerId"] = @event.Data.HunterPlayerId }
                }
            ]
        };

    public static GameLogView Apply(IEvent<HunterRevengeShotFired> @event, GameLogView view) =>
        view with
        {
            Entries =
            [
                .. view.Entries,
                new GameLogEntry
                {
                    Type = "hunter_revenge_shot_fired",
                    Data = new()
                    {
                        ["hunterPlayerId"] = @event.Data.HunterPlayerId,
                        ["targetPlayerId"] = @event.Data.TargetPlayerId
                    }
                }
            ]
        };

    public static GameLogView Apply(IEvent<HunterRevengeDeclined> @event, GameLogView view) =>
        view with
        {
            Entries =
            [
                .. view.Entries,
                new GameLogEntry
                {
                    Type = "hunter_revenge_declined",
                    Data = new() { ["hunterPlayerId"] = @event.Data.HunterPlayerId }
                }
            ]
        };

    public static GameLogView Apply(IEvent<GameEnded> @event, GameLogView view) =>
        view with
        {
            Entries =
            [
                .. view.Entries,
                new GameLogEntry
                {
                    Type = "game_ended",
                    Data = new() { ["winningFaction"] = @event.Data.WinningFaction.ToString() }
                }
            ]
        };
}
