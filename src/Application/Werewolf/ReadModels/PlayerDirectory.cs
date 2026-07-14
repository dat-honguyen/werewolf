using Application.Werewolf.Lobby;
using Marten.Events.Projections;
using System;

namespace Application.Werewolf.ReadModels;

/// <summary>
/// Global PlayerId -> DisplayName lookup, sourced from every lobby a player has ever created or
/// joined (last write wins if the same id shows up with a different name later). Exists purely so
/// game logs can render human-readable names instead of raw player GUIDs -- GameState/GameLogView
/// never carry a display name themselves, only LobbyState does.
/// </summary>
public record PlayerDirectoryEntry
{
    public required Guid Id { get; init; }
    public required string DisplayName { get; init; }
}

public partial class PlayerDirectoryProjection : MultiStreamProjection<PlayerDirectoryEntry, Guid>
{
    public PlayerDirectoryProjection()
    {
        Identity<LobbyCreated>(e => e.HostPlayerId);
        Identity<PlayerJoinedLobby>(e => e.PlayerId);
    }

    public static PlayerDirectoryEntry Create(LobbyCreated @event) =>
        new() { Id = @event.HostPlayerId, DisplayName = @event.HostDisplayName };

    public static PlayerDirectoryEntry Create(PlayerJoinedLobby @event) =>
        new() { Id = @event.PlayerId, DisplayName = @event.DisplayName };

    public static PlayerDirectoryEntry Apply(LobbyCreated @event, PlayerDirectoryEntry entry) =>
        entry with { DisplayName = @event.HostDisplayName };

    public static PlayerDirectoryEntry Apply(PlayerJoinedLobby @event, PlayerDirectoryEntry entry) =>
        entry with { DisplayName = @event.DisplayName };
}
