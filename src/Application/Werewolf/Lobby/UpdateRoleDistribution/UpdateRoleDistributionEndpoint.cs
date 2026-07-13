using Application.Werewolf.Domain;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Application.Werewolf.Lobby.UpdateRoleDistribution;

public record UpdateRoleDistribution
{
    public required RoomCode RoomCode { get; init; }
    public required Guid RequestedBy { get; init; }
    public required Dictionary<Role, int> Distribution { get; init; }
}

public static class UpdateRoleDistributionEndpoint
{
    public static ProblemDetails Validate(UpdateRoleDistribution command, [ReadAggregate("RoomCode")] LobbyState state, CancellationToken cancellationToken)
    {
        foreach (var error in LobbyCommandSupport.ValidateOpen(state))
        {
            return new ProblemDetails { Title = error };
        }

        foreach (var error in LobbyCommandSupport.ValidateHost(state, command.RequestedBy))
        {
            return new ProblemDetails { Title = error };
        }

        foreach (var error in LobbyCommandSupport.ValidateRoleDistribution(command.Distribution, state.Players.Count))
        {
            return new ProblemDetails { Title = error };
        }

        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/v1/lobby/roles")]
    public static Events Handle(UpdateRoleDistribution command, [WriteAggregate("RoomCode")] LobbyState state) =>
        [new RoleDistributionUpdated { Distribution = command.Distribution, UpdatedBy = command.RequestedBy, UpdatedAtUtc = DateTime.UtcNow }];
}
