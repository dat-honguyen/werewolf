using Application.Werewolf.Domain;
using Application.Werewolf.Game;
using Application.Werewolf.ReadModels;
using Marten;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Werewolf.Maintenance;

/// <summary>
/// Nightly janitor: this is just a game with no data-retention requirement, so rooms that have gone
/// quiet get wiped rather than kept around indefinitely (chat history included -- see
/// <see cref="ReadModels.RoomChatLogView"/> and <see cref="ReadModels.PackChatLogView"/>). Runs
/// once a day at <see cref="RunAtUtc"/> (21:00 UTC =
/// 04:00 UTC+7, i.e. the low-traffic window). A room is "inactive" once <see cref="InactivityThreshold"/>
/// has passed since the last event on either its Lobby stream or any of its Game streams (one per
/// StartGame/rematch -- see RematchLobbyEndpoint's doc comment on why each round gets a fresh GameId).
/// If literally no room is active, the whole store is wiped in one shot instead of iterating.
/// </summary>
public sealed class RoomCleanupHostedService(
    IDocumentStore store,
    ILogger<RoomCleanupHostedService> logger) : BackgroundService
{
    public static readonly TimeSpan InactivityThreshold = TimeSpan.FromHours(2);

    /// <summary>21:00 UTC == 04:00 UTC+7 -- see this class's doc comment.</summary>
    public static readonly TimeSpan RunAtUtc = TimeSpan.FromHours(21);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeUntilNextRun(DateTime.UtcNow), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await RunCleanupAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // Swallow and retry tomorrow -- a failed cleanup pass should never take the host down
                // (BackgroundServiceExceptionBehavior.StopHost applies to unhandled exceptions here).
                logger.LogError(ex, "Room cleanup pass failed");
            }
        }
    }

    internal static TimeSpan TimeUntilNextRun(DateTime utcNow)
    {
        var todayRun = utcNow.Date + RunAtUtc;
        var nextRun = utcNow < todayRun ? todayRun : todayRun.AddDays(1);
        return nextRun - utcNow;
    }

    internal async Task RunCleanupAsync(CancellationToken ct)
    {
        await using var session = store.LightweightSession();

        var lobbies = await session.Query<RoomLobbyView>()
            .Select(x => new { x.Id, x.RoomCode })
            .ToListAsync(ct);
        var games = await session.Query<GameFlowTrigger>()
            .Select(x => new { x.Id, x.RoomCode })
            .ToListAsync(ct);

        var roomCodes = lobbies.Select(x => x.RoomCode).Concat(games.Select(x => x.RoomCode)).Distinct();
        var rooms = roomCodes
            .Select(roomCode => new RoomStreams
            {
                RoomCode = roomCode,
                LobbyIds = lobbies.Where(x => x.RoomCode == roomCode).Select(x => x.Id).ToList(),
                GameIds = games.Where(x => x.RoomCode == roomCode).Select(x => x.Id).ToList()
            })
            .ToList();

        var now = DateTimeOffset.UtcNow;
        var inactiveRooms = new List<RoomStreams>();
        var anyActive = false;

        foreach (var room in rooms)
        {
            var lastActivity = await LastActivityAsync(session, room, ct);
            if (lastActivity is { } t && now - t <= InactivityThreshold)
            {
                anyActive = true;
            }
            else
            {
                inactiveRooms.Add(room);
            }
        }

        if (!anyActive)
        {
            logger.LogInformation("Room cleanup: no active rooms, wiping the entire database ({RoomCount} room(s) tracked)", rooms.Count);
            await store.Advanced.Clean.CompletelyRemoveAllAsync(ct);
            return;
        }

        foreach (var room in inactiveRooms)
        {
            await PurgeRoomAsync(session, room, ct);
        }

        await session.SaveChangesAsync(ct);
        logger.LogInformation("Room cleanup: purged {Count} inactive room(s), {ActiveCount} still active", inactiveRooms.Count, rooms.Count - inactiveRooms.Count);
    }

    private static async Task<DateTimeOffset?> LastActivityAsync(IDocumentSession session, RoomStreams room, CancellationToken ct)
    {
        DateTimeOffset? last = null;
        foreach (var streamId in room.LobbyIds.Concat(room.GameIds))
        {
            var state = await session.Events.FetchStreamStateAsync(streamId, ct);
            if (state is null)
            {
                continue;
            }

            if (last is null || state.LastTimestamp > last)
            {
                last = state.LastTimestamp;
            }
        }

        return last;
    }

    private static async Task PurgeRoomAsync(IDocumentSession session, RoomStreams room, CancellationToken ct)
    {
        foreach (var lobbyId in room.LobbyIds)
        {
            await session.DocumentStore.Advanced.Clean.DeleteSingleEventStreamAsync(lobbyId, ct: ct);
            session.Delete<RoomLobbyView>(lobbyId);
            session.Delete<RoomChatLogView>(lobbyId);
        }

        foreach (var gameId in room.GameIds)
        {
            await session.DocumentStore.Advanced.Clean.DeleteSingleEventStreamAsync(gameId, ct: ct);
            session.Delete<GameFlowTrigger>(gameId);
            session.Delete<PackChatLogView>(gameId);
            session.Delete<GameLogView>(gameId);
            session.DeleteWhere<PlayerGameView>(x => x.GameId == gameId);
        }
    }

    private sealed record RoomStreams
    {
        public required RoomCode RoomCode { get; init; }
        public required List<Guid> LobbyIds { get; init; }
        public required List<Guid> GameIds { get; init; }
    }
}
