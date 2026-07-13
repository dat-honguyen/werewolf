using Application.Werewolf.Domain;
using System;
using System.Collections.Generic;

namespace Application.Werewolf.Lobby;

public record LobbyCreated
{
    public required Guid LobbyId { get; init; }
    public required RoomCode RoomCode { get; init; }
    public required Guid HostPlayerId { get; init; }
    public required string HostDisplayName { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
}

public record PlayerJoinedLobby
{
    public required Guid PlayerId { get; init; }
    public required string DisplayName { get; init; }
    public required DateTime JoinedAtUtc { get; init; }
}

public record PlayerLeftLobby
{
    public required Guid PlayerId { get; init; }
    public required DateTime LeftAtUtc { get; init; }
}

public record PlayerKickedFromLobby
{
    public required Guid PlayerId { get; init; }
    public required Guid KickedBy { get; init; }
    public required DateTime KickedAtUtc { get; init; }
}

public record HostTransferred
{
    public required Guid PreviousHostPlayerId { get; init; }
    public required Guid NewHostPlayerId { get; init; }
    public required DateTime TransferredAtUtc { get; init; }
}

public record PlayerReadyStatusChanged
{
    public required Guid PlayerId { get; init; }
    public required bool IsReady { get; init; }
    public required DateTime UpdatedAtUtc { get; init; }
}

public record RoleDistributionUpdated
{
    public required Dictionary<Role, int> Distribution { get; init; }
    public required Guid UpdatedBy { get; init; }
    public required DateTime UpdatedAtUtc { get; init; }
}

public record GameSettingsUpdated
{
    public required GameSettings Settings { get; init; }
    public required Guid UpdatedBy { get; init; }
    public required DateTime UpdatedAtUtc { get; init; }
}

public record GameStarting
{
    public required Guid StartedBy { get; init; }
    public required DateTime StartedAtUtc { get; init; }
}

public record LobbyClosed
{
    public required DateTime ClosedAtUtc { get; init; }
}

public record LobbyCancelled
{
    public required Guid CancelledBy { get; init; }
    public required DateTime CancelledAtUtc { get; init; }
}
