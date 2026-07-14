using Application.Werewolf.Domain;
using Application.Werewolf.Game;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;

namespace Application.Werewolf.Lobby.StartGame;

public record StartGame
{
    public required RoomCode RoomCode { get; init; }
    public required Guid RequestedBy { get; init; }
    public required bool ForceStart { get; init; }
}

public record StartGameResponse
{
    public required Guid GameId { get; init; }
    public required string RoomCode { get; init; }
}

/// <summary>
/// Bridges the Lobby stream (append GameStarting/LobbyClosed via the declarative [WriteAggregate])
/// and a brand-new Game stream (StartStream) in one handler. Both operations share the same
/// injected IDocumentSession, so they still commit together in the single SaveChangesAsync
/// Wolverine issues at the end of the HTTP chain.
/// </summary>
public static class StartGameEndpoint
{
    public static ProblemDetails Validate(StartGame command, [ReadAggregate("RoomCode")] LobbyState lobby)
    {
        if (lobby.Status != LobbyStatus.Open)
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Lobby is not open." };
        }

        if (lobby.HostPlayerId != command.RequestedBy)
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Only the host can start the game." };
        }

        if (lobby.Players.Count < lobby.Settings.MinPlayers)
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = $"At least {lobby.Settings.MinPlayers} players are required." };
        }

        if (!command.ForceStart && !lobby.AllPlayersReady())
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "All players must be ready." };
        }

        if (command.ForceStart && !lobby.Settings.AllowForceStart)
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Force start is disabled." };
        }

        var roleErrors = LobbyCommandSupport.ValidateRoleDistribution(lobby.RoleDistribution, lobby.Players.Count).ToList();
        if (roleErrors.Count > 0)
        {
            return new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = string.Join(" ", roleErrors) };
        }

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/v1/lobby/start")]
    public static (StartGameResponse, Events) Handle(
        StartGame command,
        [WriteAggregate("RoomCode")] LobbyState lobby,
        IDocumentSession session)
    {
        var assignments = LobbyCommandSupport.AssignRoles(lobby);
        var now = DateTime.UtcNow;

        var gameId = Guid.NewGuid();
        session.Events.StartStream<GameState>(
            gameId,
            new GameStarted
            {
                GameId = gameId,
                RoomCode = lobby.RoomCode,
                StartedBy = command.RequestedBy,
                Settings = lobby.Settings,
                StartedAtUtc = now
            },
            new RolesAssigned { Assignments = assignments },
            new NightStarted
            {
                GameId = gameId,
                NightNumber = 1,
                StartedAtUtc = now
            });

        Events lobbyEvents =
        [
            new GameStarting { StartedBy = command.RequestedBy, StartedAtUtc = now },
            new LobbyClosed { ClosedAtUtc = now }
        ];

        return (new StartGameResponse { GameId = gameId, RoomCode = lobby.RoomCode.Value }, lobbyEvents);
    }
}
