using Application.Werewolf.Domain;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;

namespace Application.Werewolf.Lobby.UpdateGameSettings;

public record UpdateGameSettings
{
    public required RoomCode RoomCode { get; init; }
    public required Guid RequestedBy { get; init; }
    public required GameSettings Settings { get; init; }
}

[MartenStore(typeof(IWerewolfStore))]
public static class UpdateGameSettingsEndpoint
{
    public static ProblemDetails Validate(UpdateGameSettings command, [ReadAggregate("RoomCode")] LobbyState state, CancellationToken cancellationToken)
    {
        foreach (var error in LobbyCommandSupport.ValidateOpen(state))
        {
            return new ProblemDetails { Title = error };
        }

        foreach (var error in LobbyCommandSupport.ValidateHost(state, command.RequestedBy))
        {
            return new ProblemDetails { Title = error };
        }

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/v1/lobby/settings")]
    public static Events Handle(UpdateGameSettings command, [WriteAggregate("RoomCode")] LobbyState state) =>
        [new GameSettingsUpdated { Settings = command.Settings, UpdatedBy = command.RequestedBy, UpdatedAtUtc = DateTime.UtcNow }];
}
