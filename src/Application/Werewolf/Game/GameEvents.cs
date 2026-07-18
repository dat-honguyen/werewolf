using Application.Werewolf.Domain;
using System;
using System.Collections.Generic;

namespace Application.Werewolf.Game;

public record GameStarted
{
    public required Guid GameId { get; init; }
    public required RoomCode RoomCode { get; init; }
    public required Guid StartedBy { get; init; }
    public required GameSettings Settings { get; init; }
    public required DateTime StartedAtUtc { get; init; }
}

public record RolesAssigned
{
    public required Dictionary<Guid, Role> Assignments { get; init; }
}

public record NightStarted
{
    public required Guid GameId { get; init; }
    public required int NightNumber { get; init; }
    public required DateTime StartedAtUtc { get; init; }
}

public record CupidPairedLovers
{
    public required Guid GameId { get; init; }
    public required Guid CupidPlayerId { get; init; }
    public required Guid FirstPlayerId { get; init; }
    public required Guid SecondPlayerId { get; init; }
}

public record WerewolfVoteCast
{
    public required Guid GameId { get; init; }
    public required Guid WolfPlayerId { get; init; }
    public Guid? TargetPlayerId { get; init; }
}

public record WerewolfTargetLocked
{
    public required Guid GameId { get; init; }
    public Guid? TargetPlayerId { get; init; }
}

public record DoctorProtectionChosen
{
    public required Guid GameId { get; init; }
    public required Guid DoctorPlayerId { get; init; }
    public required Guid ProtectedPlayerId { get; init; }
}

public record SeerInspectionPerformed
{
    public required Guid GameId { get; init; }
    public required Guid SeerPlayerId { get; init; }
    public required Guid TargetPlayerId { get; init; }
    public required bool IsWerewolf { get; init; }
}

public record WitchHealUsed
{
    public required Guid GameId { get; init; }
    public required Guid WitchPlayerId { get; init; }
}

public record WitchPoisonUsed
{
    public required Guid GameId { get; init; }
    public required Guid WitchPlayerId { get; init; }
    public required Guid TargetPlayerId { get; init; }
}

public record WitchPassed
{
    public required Guid GameId { get; init; }
    public required Guid WitchPlayerId { get; init; }
}

public record NightResolved
{
    public required List<Guid> NightDeaths { get; init; }
}

public record HunterRevengePending
{
    public required Guid GameId { get; init; }
    public required Guid HunterPlayerId { get; init; }
}

public record HunterRevengeShotFired
{
    public required Guid GameId { get; init; }
    public required Guid HunterPlayerId { get; init; }
    public required Guid TargetPlayerId { get; init; }
}

public record HunterRevengeDeclined
{
    public required Guid GameId { get; init; }
    public required Guid HunterPlayerId { get; init; }
}

public record DayStarted
{
    public required Guid GameId { get; init; }
    public required int DayNumber { get; init; }
    public required DateTime StartedAtUtc { get; init; }
}

public record VotingStarted
{
    public required Guid GameId { get; init; }
    public required DateTime StartedAtUtc { get; init; }
}

public record VoteCast
{
    public required Guid GameId { get; init; }
    public required Guid VoterPlayerId { get; init; }
    public Guid? TargetPlayerId { get; init; }
}

public record VotingClosed
{
    public required DateTime ClosedAtUtc { get; init; }
}

public record LynchTargetDetermined
{
    public required Guid TargetPlayerId { get; init; }
}

public record NoLynchOccurred;

public record PlayerLynched
{
    public required Guid GameId { get; init; }
    public required Guid PlayerId { get; init; }
}

public record PlayerDied
{
    public required Guid GameId { get; init; }
    public required Guid PlayerId { get; init; }
    public required string Cause { get; init; }
}

public record GameEnded
{
    public required Guid GameId { get; init; }
    public required WinningFaction WinningFaction { get; init; }
    public required DateTime EndedAtUtc { get; init; }
}

/// <summary>
/// A message sent to everyone in the room ("Town Square"). Appended to the LobbyState stream
/// (LobbyId), not GameState -- the lobby aggregate spans the room's whole lifetime (open, playing,
/// closed, reopened for a rematch), so chat keeps working before a game has started and across
/// rematches instead of resetting each round the way GameId-keyed data does. Only
/// RoomChatLogViewProjection reacts to this.
/// </summary>
public record RoomChatMessageSent
{
    public required Guid LobbyId { get; init; }
    public required Guid SenderId { get; init; }
    public required string Text { get; init; }
    public required DateTime SentAtUtc { get; init; }
}

/// <summary>
/// A message sent to the werewolf pack only ("Pack Chat"). Deliberately never published over
/// SignalR (see Notifications/PlayerNotification.cs's comment on GetWerewolfVotesEndpoint) --
/// living werewolves poll GetPackChatEndpoint instead, which checks pack membership the same way.
/// </summary>
public record PackChatMessageSent
{
    public required Guid GameId { get; init; }
    public required Guid SenderId { get; init; }
    public required string Text { get; init; }
    public required DateTime SentAtUtc { get; init; }
}
