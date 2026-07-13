using Application.Werewolf.Game;
using JasperFx.MultiTenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace Application.Werewolf;

public interface IWerewolfStore : IDocumentStore;

public static class WerewolfModule
{
    public static WebApplicationBuilder AddWerewolf(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;

        services
            .AddMartenStore<IWerewolfStore>(so =>
            {
                so.Events.TenancyStyle = TenancyStyle.Single;
                so.Connection(configuration.GetConnectionString("exploratory-conversations")!);
                so.DatabaseSchemaName = "werewolf";
                WerewolfMartenModule.Configure(so);
            })
            .IntegrateWithWolverine()
            // Forward committed Game events to the notification handlers (Werewolf/Notifications),
            // which turn them into SignalR pushes to room/player groups.
            .PublishEventsToWolverine("WerewolfNotifications", x =>
            {
                x.PublishEvent<GameStarted>();
                x.PublishEvent<NightStarted>();
                x.PublishEvent<DayStarted>();
                x.PublishEvent<VotingStarted>();
                x.PublishEvent<PlayerDied>();
                x.PublishEvent<PlayerLynched>();
                x.PublishEvent<SeerInspectionPerformed>();
                x.PublishEvent<GameEnded>();
            });

        return builder;
    }
}
