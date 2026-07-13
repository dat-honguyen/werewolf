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

    [NaturalKeySource]
    public void Apply(LobbyCreated @event)
    {
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
        Players[@event.PlayerId] = new()
        {
            PlayerId = @event.PlayerId,
            DisplayName = @event.DisplayName,
            IsReady = false
        };
    }

    public void Apply(PlayerLeftLobby @event)
    {
        Players.Remove(@event.PlayerId);
    }

    public void Apply(PlayerKickedFromLobby @event)
    {
        Players.Remove(@event.PlayerId);
    }

    public void Apply(HostTransferred @event)
    {
        HostPlayerId = @event.NewHostPlayerId;
    }

    public void Apply(PlayerReadyStatusChanged @event)
    {
        if (Players.TryGetValue(@event.PlayerId, out var player))
        {
            Players[@event.PlayerId] = player with { IsReady = @event.IsReady };
        }
    }

    public void Apply(RoleDistributionUpdated @event)
    {
        RoleDistribution = @event.Distribution;
    }

    public void Apply(GameSettingsUpdated @event)
    {
        Settings = @event.Settings;
    }

    public void Apply(GameStarting _)
    {
        Status = LobbyStatus.Starting;
    }

    public void Apply(LobbyClosed _)
    {
        Status = LobbyStatus.Closed;
    }

    public void Apply(LobbyCancelled _)
    {
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

