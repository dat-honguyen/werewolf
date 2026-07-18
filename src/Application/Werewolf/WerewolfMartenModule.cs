using Application.Werewolf.Game;
using Application.Werewolf.Lobby;
using Application.Werewolf.ReadModels;
using JasperFx.Events.Projections;
using static JasperFx.Events.Projections.ProjectionLifecycle;

namespace Application.Werewolf;

public static class WerewolfMartenModule
{
    public static void Configure(StoreOptions options)
    {
        options.Events
            .AddEventType<LobbyCreated>()
            .AddEventType<PlayerJoinedLobby>()
            .AddEventType<PlayerLeftLobby>()
            .AddEventType<PlayerKickedFromLobby>()
            .AddEventType<HostTransferred>()
            .AddEventType<PlayerReadyStatusChanged>()
            .AddEventType<RoleDistributionUpdated>()
            .AddEventType<GameSettingsUpdated>()
            .AddEventType<GameStarting>()
            .AddEventType<LobbyClosed>()
            .AddEventType<LobbyCancelled>()
            .AddEventType<GameStarted>()
            .AddEventType<RolesAssigned>()
            .AddEventType<NightStarted>()
            .AddEventType<CupidPairedLovers>()
            .AddEventType<WerewolfVoteCast>()
            .AddEventType<WerewolfTargetLocked>()
            .AddEventType<DoctorProtectionChosen>()
            .AddEventType<SeerInspectionPerformed>()
            .AddEventType<WitchHealUsed>()
            .AddEventType<WitchPoisonUsed>()
            .AddEventType<WitchPassed>()
            .AddEventType<NightResolved>()
            .AddEventType<HunterRevengePending>()
            .AddEventType<HunterRevengeShotFired>()
            .AddEventType<HunterRevengeDeclined>()
            .AddEventType<DayStarted>()
            .AddEventType<VotingStarted>()
            .AddEventType<VoteCast>()
            .AddEventType<VotingClosed>()
            .AddEventType<LynchTargetDetermined>()
            .AddEventType<NoLynchOccurred>()
            .AddEventType<PlayerLynched>()
            .AddEventType<PlayerDied>()
            .AddEventType<GameEnded>()
            .AddEventType<RoomChatMessageSent>()
            .AddEventType<PackChatMessageSent>();

        options.Projections.LiveStreamAggregation<LobbyState>();
        options.Projections.LiveStreamAggregation<GameState>();

        options.Projections.Add<RoomLobbyViewProjection>(Async);
        options.Projections.Add<PlayerGameViewProjection>(Async);

        // Inline (not Async) so the GET log/directory endpoints read back-immediately-consistent
        // data right after the triggering command, with no eventual-consistency polling needed.
        options.Projections.Add<GameLogViewProjection>(ProjectionLifecycle.Inline);
        options.Projections.Add<PlayerDirectoryProjection>(ProjectionLifecycle.Inline);
        options.Projections.Add<RoomChatLogViewProjection>(ProjectionLifecycle.Inline);
        options.Projections.Add<PackChatLogViewProjection>(ProjectionLifecycle.Inline);

        // Inline (not Async): this is the sole trigger for TryResolveNight/TryCloseVoting, the only
        // code that ever moves the game out of Night/DayVoting. Leaving it Async made phase
        // advancement depend entirely on the async daemon's polling/leader-election health with no
        // synchronous fallback -- a night action could be accepted and the game could still get stuck
        // in Night forever if that daemon shard wasn't running. Inline ties it to the same durable
        // outbox path the local queues already use, removing that dependency.
        options.Projections.Add<GameFlowTriggerProjection>(ProjectionLifecycle.Inline);

        options.Events.UseOptimizedProjectionRebuilds = true;
        options.Projections.Errors.SkipApplyErrors = false;
        options.Projections.Errors.SkipSerializationErrors = false;
        options.Projections.Errors.SkipUnknownEvents = false;
    }
}
