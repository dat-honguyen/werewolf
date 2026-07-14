using Application.Werewolf.Domain;
using Application.Werewolf.Game;
using Marten.Events.Projections;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Application.Werewolf.ReadModels;

public record PlayerGameView
{
    public required string Id { get; init; }
    public required Guid GameId { get; init; }
    public required Guid PlayerId { get; init; }
    public required Role Role { get; init; }
    public required bool IsAlive { get; init; }
    public required GamePhase Phase { get; init; }
    public required int NightNumber { get; init; }
    public required int DayNumber { get; init; }
    public string? LastSeerResult { get; init; }
}

public class PlayerGameViewProjection : MultiStreamProjection<PlayerGameView, string>
{
    public const int VERSION = 1;

    public PlayerGameViewProjection()
    {
        Version = VERSION;

        IncludeType<GameStarted>();
        IncludeType<RolesAssigned>();
        IncludeType<NightStarted>();
        IncludeType<DayStarted>();
        IncludeType<VotingStarted>();
        IncludeType<PlayerDied>();
        IncludeType<GameEnded>();
        IncludeType<SeerInspectionPerformed>();
        IncludeType<WerewolfVoteCast>();

        CustomGrouping(async (session, events, group) =>
        {
            foreach (var @event in events)
            {
                var gameId = @event.StreamId;

                switch (@event.Data)
                {
                    case RolesAssigned assigned:
                        foreach (var playerId in assigned.Assignments.Keys)
                        {
                            group.AddEvent(ToId(gameId, playerId), @event);
                        }

                        break;

                    case SeerInspectionPerformed seer:
                        group.AddEvent(ToId(gameId, seer.SeerPlayerId), @event);
                        break;

                    case WerewolfVoteCast:
                        var werewolfIds = await session.Query<PlayerGameView>()
                            .Where(x => x.GameId == gameId && x.Role == Role.Werewolf)
                            .Select(x => x.PlayerId)
                            .ToListAsync();

                        foreach (var playerId in werewolfIds)
                        {
                            group.AddEvent(ToId(gameId, playerId), @event);
                        }

                        break;

                    default:
                        var playerIds = await session.Query<PlayerGameView>()
                            .Where(x => x.GameId == gameId)
                            .Select(x => x.PlayerId)
                            .ToListAsync();

                            foreach (var playerId in playerIds)
                        {
                            group.AddEvent(ToId(gameId, playerId), @event);
                        }

                        break;
                }
            }
        });
    }

    public override (PlayerGameView?, ActionType) DetermineAction(
        PlayerGameView? snapshot,
        string identity,
        IReadOnlyList<IEvent> events)
    {
        var current = snapshot;
        var playerId = ParsePlayerId(identity);
        var gameId = ParseGameId(identity);

        foreach (var @event in events)
        {
            switch (@event.Data)
            {
                case RolesAssigned assigned when assigned.Assignments.TryGetValue(playerId, out var role):
                    current = new()
                    {
                        Id = identity,
                        GameId = gameId,
                        PlayerId = playerId,
                        Role = role,
                        IsAlive = true,
                        Phase = GamePhase.RoleAssignment,
                        NightNumber = 0,
                        DayNumber = 0,
                        LastSeerResult = null
                    };
                    break;

                case NightStarted night when current is not null:
                    current = current with { Phase = GamePhase.Night, NightNumber = night.NightNumber };
                    break;

                case DayStarted day when current is not null:
                    current = current with { Phase = GamePhase.DayDiscussion, DayNumber = day.DayNumber };
                    break;

                case VotingStarted when current is not null:
                    current = current with { Phase = GamePhase.DayVoting };
                    break;

                case PlayerDied died when current is not null && died.PlayerId == playerId:
                    current = current with { IsAlive = false };
                    break;

                case SeerInspectionPerformed seer when current is not null && seer.SeerPlayerId == playerId:
                    current = current with { LastSeerResult = $"{seer.TargetPlayerId}:{(seer.IsWerewolf ? "Werewolf" : "NotWerewolf")}" };
                    break;

                case GameEnded when current is not null:
                    current = current with { Phase = GamePhase.GameOver };
                    break;
            }
        }

        return current is null ? (null, ActionType.Nothing) : (current, ActionType.Store);
    }

    private static string ToId(Guid gameId, Guid playerId) => $"{gameId:N}:{playerId:N}";

    private static Guid ParseGameId(string id) => Guid.Parse(id.Split(':')[0]);

    private static Guid ParsePlayerId(string id) => Guid.Parse(id.Split(':')[1]);
}
