using Application.Werewolf.Domain;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Application.Werewolf.Lobby;

public class LobbyState
{
    public Guid Id { get; set; }

    [NaturalKey]
    public RoomCode RoomCode { get; set; } = default;

    public LobbyStatus Status { get; set; } = LobbyStatus.Open;

    public Guid HostPlayerId { get; set; }

    public Dictionary<Guid, LobbyPlayer> Players { get; set; } = new();

    public GameSettings Settings { get; set; } = GameSettings.Default();

    public Dictionary<Role, int> RoleDistribution { get; set; } = GameSettings.DefaultRoleDistribution();

    /// <summary>
    /// Incremented by every <c>Apply</c> below, same pattern as <see cref="Game.GameState.Version"/>
    /// -- a monotonic sequence number for the client version-gap resync (see GetLobbyEndpoint and
    /// RoomLobbyViewProjection.RaiseSideEffects).
    /// </summary>
    public long Version { get; set; }

    [NaturalKeySource]
    public void Apply(LobbyCreated @event)
    {
        Version++;
        Id = @event.LobbyId;
        RoomCode = @event.RoomCode;
        Status = LobbyStatus.Open;
        HostPlayerId = @event.HostPlayerId;
        Players[@event.HostPlayerId] = new()
        {
            PlayerId = @event.HostPlayerId,
            DisplayName = @event.HostDisplayName,
            IsReady = true
        };
    }

    public void Apply(PlayerJoinedLobby @event)
    {
        Version++;
        Players[@event.PlayerId] = new()
        {
            PlayerId = @event.PlayerId,
            DisplayName = @event.DisplayName,
            IsReady = false
        };
    }

    public void Apply(PlayerLeftLobby @event)
    {
        Version++;
        Players.Remove(@event.PlayerId);
    }

    public void Apply(PlayerKickedFromLobby @event)
    {
        Version++;
        Players.Remove(@event.PlayerId);
    }

    public void Apply(HostTransferred @event)
    {
        Version++;
        HostPlayerId = @event.NewHostPlayerId;
    }

    public void Apply(PlayerReadyStatusChanged @event)
    {
        Version++;
        if (Players.TryGetValue(@event.PlayerId, out var player))
        {
            Players[@event.PlayerId] = player with { IsReady = @event.IsReady };
        }
    }

    public void Apply(RoleDistributionUpdated @event)
    {
        Version++;
        RoleDistribution = @event.Distribution;
    }

    public void Apply(GameSettingsUpdated @event)
    {
        Version++;
        Settings = @event.Settings;
    }

    public void Apply(GameStarting _)
    {
        Version++;
        Status = LobbyStatus.Starting;
    }

    public void Apply(LobbyClosed _)
    {
        Version++;
        Status = LobbyStatus.Closed;
    }

    public void Apply(LobbyCancelled _)
    {
        Version++;
        Status = LobbyStatus.Cancelled;
    }

    public bool AllPlayersReady() => Players.Values.All(x => x.IsReady);
}

public record LobbyPlayer
{
    public required Guid PlayerId { get; init; }
    public required string DisplayName { get; init; }
    public required bool IsReady { get; init; }
}

