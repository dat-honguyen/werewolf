using Application.Werewolf.Game;
using Marten.Events.Aggregation;
using System;
using System.Collections.Generic;

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

    public static GameLogView Apply(IEvent<NightStarted> @event, GameLogView view) =>
        view with { Entries = [.. view.Entries, $"Night {@event.Data.NightNumber} started"] };

    public static GameLogView Apply(IEvent<DayStarted> @event, GameLogView view) =>
        view with { Entries = [.. view.Entries, $"Day {@event.Data.DayNumber} started"] };

    public static GameLogView Apply(IEvent<PlayerDied> @event, GameLogView view) =>
        view with { Entries = [.. view.Entries, $"Player {@event.Data.PlayerId} died ({@event.Data.Cause})"] };

    public static GameLogView Apply(IEvent<PlayerLynched> @event, GameLogView view) =>
        view with { Entries = [.. view.Entries, $"Player {@event.Data.PlayerId} was lynched"] };

    public static GameLogView Apply(IEvent<NoLynchOccurred> _, GameLogView view) =>
        view with { Entries = [.. view.Entries, "No one was lynched"] };

    public static GameLogView Apply(IEvent<GameEnded> @event, GameLogView view) =>
        view with { Entries = [.. view.Entries, $"Game ended. Winner: {@event.Data.WinningFaction}"] };
}
