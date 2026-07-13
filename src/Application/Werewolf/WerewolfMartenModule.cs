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
            .AddEventType<GameEnded>();

        options.Projections.LiveStreamAggregation<LobbyState>();
        options.Projections.LiveStreamAggregation<GameState>();

        options.Projections.Add<RoomLobbyViewProjection>(Async);
        options.Projections.Add<PlayerGameViewProjection>(Async);
        options.Projections.Add<GameLogViewProjection>(Async);

        options.Events.UseOptimizedProjectionRebuilds = true;
        options.Projections.Errors.SkipApplyErrors = false;
        options.Projections.Errors.SkipSerializationErrors = false;
        options.Projections.Errors.SkipUnknownEvents = false;
    }
}
