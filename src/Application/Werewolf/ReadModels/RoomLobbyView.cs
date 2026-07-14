using Application.Werewolf.Domain;
using Application.Werewolf.Lobby;
using Application.Werewolf.Notifications;
using Marten.Events.Aggregation;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Application.Werewolf.ReadModels;

public record RoomLobbyView
{
    public required Guid Id { get; init; }
    public required RoomCode RoomCode { get; init; }
    public required LobbyStatus Status { get; init; }
    public required Guid HostPlayerId { get; init; }
    public required Dictionary<Guid, RoomLobbyPlayerView> Players { get; init; }
    public required Dictionary<Role, int> RoleDistribution { get; init; }
    public required GameSettings Settings { get; init; }
}

public record RoomLobbyPlayerView
{
    public required Guid PlayerId { get; init; }
    public required string DisplayName { get; init; }
    public required bool IsReady { get; init; }
}

public partial class RoomLobbyViewProjection : SingleStreamProjection<RoomLobbyView, Guid>
{
    public const int VERSION = 1;

    public RoomLobbyViewProjection()
    {
        Version = VERSION;
    }

    public static RoomLobbyView Create(IEvent<LobbyCreated> @event) =>
        new()
        {
            Id = @event.Data.LobbyId,
            RoomCode = @event.Data.RoomCode,
            Status = LobbyStatus.Open,
            HostPlayerId = @event.Data.HostPlayerId,
            Players = new Dictionary<Guid, RoomLobbyPlayerView>
            {
                [@event.Data.HostPlayerId] = new()
                {
                    PlayerId = @event.Data.HostPlayerId,
                    DisplayName = @event.Data.HostDisplayName,
                    IsReady = true
                }
            },
            RoleDistribution = GameSettings.DefaultRoleDistribution(),
            Settings = GameSettings.Default()
        };

    public static RoomLobbyView Apply(IEvent<PlayerJoinedLobby> @event, RoomLobbyView view)
    {
        var players = new Dictionary<Guid, RoomLobbyPlayerView>(view.Players)
        {
            [@event.Data.PlayerId] = new()
            {
                PlayerId = @event.Data.PlayerId,
                DisplayName = @event.Data.DisplayName,
                IsReady = false
            }
        };

        return view with { Players = players };
    }

    public static RoomLobbyView Apply(IEvent<PlayerLeftLobby> @event, RoomLobbyView view)
    {
        var players = new Dictionary<Guid, RoomLobbyPlayerView>(view.Players);
        players.Remove(@event.Data.PlayerId);
        return view with { Players = players };
    }

    public static RoomLobbyView Apply(IEvent<PlayerKickedFromLobby> @event, RoomLobbyView view)
    {
        var players = new Dictionary<Guid, RoomLobbyPlayerView>(view.Players);
        players.Remove(@event.Data.PlayerId);
        return view with { Players = players };
    }

    public static RoomLobbyView Apply(IEvent<HostTransferred> @event, RoomLobbyView view) =>
        view with { HostPlayerId = @event.Data.NewHostPlayerId };

    public static RoomLobbyView Apply(IEvent<PlayerReadyStatusChanged> @event, RoomLobbyView view)
    {
        if (!view.Players.TryGetValue(@event.Data.PlayerId, out var player))
        {
            return view;
        }

        var players = new Dictionary<Guid, RoomLobbyPlayerView>(view.Players)
        {
            [@event.Data.PlayerId] = player with { IsReady = @event.Data.IsReady }
        };

        return view with { Players = players };
    }

    public static RoomLobbyView Apply(IEvent<RoleDistributionUpdated> @event, RoomLobbyView view) =>
        view with { RoleDistribution = @event.Data.Distribution };

    public static RoomLobbyView Apply(IEvent<GameSettingsUpdated> @event, RoomLobbyView view) =>
        view with { Settings = @event.Data.Settings };

    public static RoomLobbyView Apply(IEvent<LobbyClosed> _, RoomLobbyView view) =>
        view with { Status = LobbyStatus.Closed };

    public static RoomLobbyView Apply(IEvent<LobbyCancelled> _, RoomLobbyView view) =>
        view with { Status = LobbyStatus.Cancelled };

    /// <summary>
    /// Pushes a SignalR notification once this projection's update actually lands, same pattern as
    /// <see cref="Game.GameFlowTriggerProjection"/> — clients re-fetch full lobby state via GET on
    /// receipt (see RoomLobbyView), so all of these collapse to a single generic "lobby.updated" kind.
    /// LobbyCreated is excluded: no one else is in the room yet to notify.
    /// </summary>
    public override ValueTask RaiseSideEffects(IDocumentOperations ops, IEventSlice<RoomLobbyView> slice)
    {
        var view = slice.Snapshot;
        if (view is null)
        {
            return ValueTask.CompletedTask;
        }

        foreach (var e in slice.Events())
        {
            switch (e.Data)
            {
                case PlayerJoinedLobby:
                case PlayerLeftLobby:
                case PlayerKickedFromLobby:
                case HostTransferred:
                case PlayerReadyStatusChanged:
                case RoleDistributionUpdated:
                case GameSettingsUpdated:
                case LobbyCancelled:
                case LobbyClosed:
                    slice.PublishMessage(new NotifyRoomUpdated(view.RoomCode));
                    break;
            }
        }

        return ValueTask.CompletedTask;
    }
}
