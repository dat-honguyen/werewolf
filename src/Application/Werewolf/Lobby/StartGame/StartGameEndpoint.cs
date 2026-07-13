using Application.Werewolf.Domain;
using Application.Werewolf.Game;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
/// Bridges the Lobby stream (append GameStarting/LobbyClosed) and a brand-new Game stream
/// (StartStream) atomically in one transaction. Wolverine's [WriteAggregate] does not support
/// combining an existing-stream append with a new-stream start in one handler, so this stays
/// a manual FetchForWriting + MartenOps.StartStream, unlike the rest of the Lobby/Game handlers.
/// </summary>
[MartenStore(typeof(IWerewolfStore))]
public static class StartGameEndpoint
{
    // Note: this handler loads the Lobby manually (see class remarks above), so there's no
    // declaratively-loaded aggregate for a Validate(command, LobbyState) method to share — the
    // Validate/Handle variable-sharing that powers railway validation elsewhere in this codebase
    // depends on a [WriteAggregate]/[Aggregate]/[ReadAggregate] parameter existing on Handle.
    // Guards stay inline exceptions here instead.
    [WolverinePost("/api/v1/lobby/start")]
    public static async Task<StartGameResponse> Handle(
        StartGame command,
        IDocumentSession session,
        CancellationToken cancellationToken)
    {
        var lobbyStream = await LobbyCommandSupport.LoadLobbyStream(session, command.RoomCode, cancellationToken);
        var lobby = lobbyStream.Aggregate ?? throw new InvalidOperationException("Lobby not found.");

        if (lobby.Status != LobbyStatus.Open)
        {
            throw new InvalidOperationException("Lobby is not open.");
        }

        if (lobby.HostPlayerId != command.RequestedBy)
        {
            throw new InvalidOperationException("Only the host can start the game.");
        }

        if (lobby.Players.Count < lobby.Settings.MinPlayers)
        {
            throw new InvalidOperationException($"At least {lobby.Settings.MinPlayers} players are required.");
        }

        if (!command.ForceStart && !lobby.AllPlayersReady())
        {
            throw new InvalidOperationException("All players must be ready.");
        }

        if (command.ForceStart && !lobby.Settings.AllowForceStart)
        {
            throw new InvalidOperationException("Force start is disabled.");
        }

        var roleErrors = LobbyCommandSupport.ValidateRoleDistribution(lobby.RoleDistribution, lobby.Players.Count).ToList();
        if (roleErrors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", roleErrors));
        }

        var assignments = LobbyCommandSupport.AssignRoles(lobby);
        var now = DateTime.UtcNow;

        lobbyStream.AppendOne(new GameStarting
        {
            StartedBy = command.RequestedBy,
            StartedAtUtc = now
        });

        lobbyStream.AppendOne(new LobbyClosed
        {
            ClosedAtUtc = now
        });

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
                NightNumber = 1,
                StartedAtUtc = now
            });

        return new()
        {
            GameId = gameId,
            RoomCode = lobby.RoomCode.Value
        };
    }
}
