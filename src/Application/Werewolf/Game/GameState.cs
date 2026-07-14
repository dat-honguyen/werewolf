using Application.Werewolf.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Application.Werewolf.Game;

public class GameState
{
    public Guid Id { get; set; }

    [NaturalKey]
    public RoomCode RoomCode { get; set; } = default;

    public GamePhase Phase { get; set; } = GamePhase.RoleAssignment;

    public Guid HostPlayerId { get; set; }

    public GameSettings Settings { get; set; } = GameSettings.Default();

    public int NightNumber { get; set; }
    public int DayNumber { get; set; }

    public Dictionary<Guid, GamePlayer> Players { get; set; } = new();

    public NightActionsState CurrentNight { get; set; } = new();

    public DayVoteState CurrentVote { get; set; } = new();

    public LoversPair? Lovers { get; set; }

    public Queue<Guid> PendingHunterRevenge { get; set; } = new();

    public GameResult? Result { get; set; }

    /// <summary>
    /// Who the Doctor protected last night, tracked outside <see cref="CurrentNight"/> (which resets
    /// every <see cref="Apply(NightStarted)"/>) so the same target can be blocked two nights in a row.
    /// </summary>
    public Guid? LastDoctorProtectedTarget { get; set; }

    [NaturalKeySource]
    public void Apply(GameStarted @event)
    {
        Id = @event.GameId;
        RoomCode = @event.RoomCode;
        HostPlayerId = @event.StartedBy;
        Settings = @event.Settings;
        Phase = GamePhase.RoleAssignment;
    }

    public void Apply(RolesAssigned @event)
    {
        Players = @event.Assignments.ToDictionary(
            x => x.Key,
            x => new GamePlayer
            {
                PlayerId = x.Key,
                Role = x.Value,
                IsAlive = true,
                HunterRevengeUsed = false
            });
    }

    public void Apply(NightStarted @event)
    {
        NightNumber = @event.NightNumber;
        CurrentNight = new NightActionsState();
        Phase = GamePhase.Night;
    }

    public void Apply(CupidPairedLovers @event)
    {
        Lovers = new(@event.FirstPlayerId, @event.SecondPlayerId);
        CurrentNight.CupidDone = true;
    }

    public void Apply(WerewolfVoteCast @event)
    {
        CurrentNight.WerewolfVotes[@event.WolfPlayerId] = @event.TargetPlayerId;
    }

    public void Apply(WerewolfTargetLocked @event)
    {
        CurrentNight.WerewolfLockedTarget = @event.TargetPlayerId;
        CurrentNight.WerewolfLocked = true;
    }

    public void Apply(DoctorProtectionChosen @event)
    {
        CurrentNight.DoctorProtectedTarget = @event.ProtectedPlayerId;
        CurrentNight.DoctorDone = true;
        LastDoctorProtectedTarget = @event.ProtectedPlayerId;
    }

    public void Apply(SeerInspectionPerformed @event)
    {
        CurrentNight.SeerInspections[@event.SeerPlayerId] = @event.TargetPlayerId;
        CurrentNight.SeerDone = true;
    }

    public void Apply(WitchHealUsed @event)
    {
        CurrentNight.WitchUsedHeal = true;
        CurrentNight.WitchDone = Settings.WitchSinglePotionPerNight || Players[@event.WitchPlayerId].WitchPoisonPotionUsed;
        Players[@event.WitchPlayerId] = Players[@event.WitchPlayerId] with { WitchHealPotionUsed = true };
    }

    public void Apply(WitchPoisonUsed @event)
    {
        CurrentNight.WitchPoisonTarget = @event.TargetPlayerId;
        CurrentNight.WitchUsedPoison = true;
        CurrentNight.WitchDone = Settings.WitchSinglePotionPerNight || Players[@event.WitchPlayerId].WitchHealPotionUsed;
        Players[@event.WitchPlayerId] = Players[@event.WitchPlayerId] with { WitchPoisonPotionUsed = true };
    }

    public void Apply(WitchPassed _)
    {
        CurrentNight.WitchDone = true;
    }

    public void Apply(NightResolved _)
    {
        CurrentNight.Resolved = true;
    }

    public void Apply(HunterRevengePending @event)
    {
        PendingHunterRevenge.Enqueue(@event.HunterPlayerId);
    }

    public void Apply(HunterRevengeShotFired @event)
    {
        Players[@event.HunterPlayerId] = Players[@event.HunterPlayerId] with { HunterRevengeUsed = true };
        _ = PendingHunterRevenge.Dequeue();
    }

    public void Apply(HunterRevengeDeclined @event)
    {
        Players[@event.HunterPlayerId] = Players[@event.HunterPlayerId] with { HunterRevengeUsed = true };
        _ = PendingHunterRevenge.Dequeue();
    }

    public void Apply(DayStarted @event)
    {
        DayNumber = @event.DayNumber;
        Phase = GamePhase.DayDiscussion;
    }

    public void Apply(VotingStarted _)
    {
        CurrentVote = new DayVoteState { Started = true };
        Phase = GamePhase.DayVoting;
    }

    public void Apply(VoteCast @event)
    {
        CurrentVote.Votes[@event.VoterPlayerId] = @event.TargetPlayerId;
    }

    public void Apply(VotingClosed _)
    {
        CurrentVote.Closed = true;
        Phase = GamePhase.DayResolution;
    }

    public void Apply(PlayerLynched _)
    {
        Phase = GamePhase.DayResolution;
    }

    public void Apply(PlayerDied @event)
    {
        if (Players.TryGetValue(@event.PlayerId, out var player))
        {
            Players[@event.PlayerId] = player with { IsAlive = false };
        }
    }

    public void Apply(GameEnded @event)
    {
        Phase = GamePhase.GameOver;
        Result = new()
        {
            WinningFaction = @event.WinningFaction,
            EndedAtUtc = @event.EndedAtUtc,
            FinalRoles = Players.ToDictionary(x => x.Key, x => x.Value.Role)
        };
    }

    public bool IsAlive(Guid playerId) => Players.TryGetValue(playerId, out var player) && player.IsAlive;

    public IEnumerable<Guid> AlivePlayers() => Players.Values.Where(x => x.IsAlive).Select(x => x.PlayerId);
}

public record GamePlayer
{
    public required Guid PlayerId { get; init; }
    public required Role Role { get; init; }
    public required bool IsAlive { get; init; }
    public required bool HunterRevengeUsed { get; init; }
    public bool WitchHealPotionUsed { get; init; }
    public bool WitchPoisonPotionUsed { get; init; }
}

public class NightActionsState
{
    public bool CupidDone { get; set; }
    public Dictionary<Guid, Guid?> WerewolfVotes { get; set; } = new();
    public Guid? WerewolfLockedTarget { get; set; }
    public bool WerewolfLocked { get; set; }
    public bool DoctorDone { get; set; }
    public Guid? DoctorProtectedTarget { get; set; }
    public bool SeerDone { get; set; }
    public Dictionary<Guid, Guid> SeerInspections { get; set; } = new();
    public bool WitchDone { get; set; }
    public bool WitchUsedHeal { get; set; }
    public bool WitchUsedPoison { get; set; }
    public Guid? WitchPoisonTarget { get; set; }
    public bool Resolved { get; set; }
}

public class DayVoteState
{
    public bool Started { get; set; }
    public bool Closed { get; set; }
    public Dictionary<Guid, Guid?> Votes { get; set; } = new();
}

public record LoversPair(Guid FirstPlayerId, Guid SecondPlayerId);

public record GameResult
{
    public required WinningFaction WinningFaction { get; init; }
    public required DateTime EndedAtUtc { get; init; }
    public required Dictionary<Guid, Role> FinalRoles { get; init; }
}
