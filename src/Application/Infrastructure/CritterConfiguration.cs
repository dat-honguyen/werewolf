using JasperFx.CodeGeneration;
using JasperFx.Core;
using JasperFx.MultiTenancy;
using JasperFx.OpenTelemetry;
using Marten.Exceptions;
using Marten.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;
using Application.Werewolf;
using Application.Werewolf.Game;
using Application.Werewolf.Maintenance;
using Application.Werewolf.Notifications;
using Weasel.Core;
using Wolverine.ErrorHandling;
using Wolverine.FluentValidation;
using Wolverine.SignalR;

namespace Application.Infrastructure;

public static class CritterConfiguration
{
    extension(WebApplicationBuilder webApplication)
    {
        public WebApplicationBuilder AddCritterPlugin()
        {
            // Register Marten (DB/event store) and Wolverine (messaging/HTTP) into the app builder.
            // This keeps startup wiring consistent and makes the Critter stack a single opt-in.
            // Ordering matters: Marten wiring is required before Wolverine integrates with it.
            webApplication
                .AddMartenPlugin()
                .AddWolverinePlugin();
            // CritterStackDefaults configures shared defaults for code generation and resource setup.
            // This is the canonical place to set environment-specific auto-create and code-gen policies.
            // These defaults affect resource creation, code generation, and type discovery at boot.
            webApplication.Services.CritterStackDefaults(x =>
            {
                // In integration tests we align Critter's dev environment name to the test env
                // so auto-create and behavior mirrors local dev but remains isolated.
                // This prevents test runs from using non-test dev conventions.
                if (webApplication.Environment.IsIntegrationOrDevelopment())
                {
                    x.DevelopmentEnvironmentName = webApplication.Environment.EnvironmentName;
                }

                x.ApplicationAssembly = typeof(Program).Assembly;
                // Dev: allow automatic creation of database objects and runtime code-gen.
                // Use AutoCreate.All only in safe environments to prevent accidental schema drift.
                x.Development.ResourceAutoCreate = AutoCreate.All;
                x.Development.GeneratedCodeMode = TypeLoadMode.Dynamic;

                // Prod: use pre-generated code and enforce schema creation policy.
                // Static code mode removes dynamic codegen at runtime for faster startup and safety.
                x.Production.GeneratedCodeMode = TypeLoadMode.Static;
                x.Production.ResourceAutoCreate = AutoCreate.All; //just a game, clear all
                // Ensure generated code exists in production to avoid missing handler/aggregate types.
                x.Production.AssertAllPreGeneratedTypesExist = true;
            });
            return webApplication;
        }

        private WebApplicationBuilder AddMartenPlugin()
        {
            var services = webApplication.Services;
            var configuration = webApplication.Configuration;

            // Give background services a brief grace period on shutdown for clean drains.
            // This helps Wolverine/Marten finish in-flight work before the host exits.
            webApplication.WebHost.UseShutdownTimeout(10.Seconds());

            // If a background service throws, stop the host to avoid partial failure states.
            // Fail-fast prevents the app from running in a degraded or inconsistent state.
            services.Configure<HostOptions>(options =>
            {
                options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
            });

            // Register Npgsql data source, configure Marten and integrate with Wolverine.
            // This sets up lightweight sessions and initializes configured schemas.
            // The Npgsql data source is shared for pooled, efficient connections.
            services
                .AddNpgsqlDataSource(configuration.GetConnectionString("database")!)
                .SetupMartenConfiguration()
                .UseNpgsqlDataSource()
                // Lightweight sessions provide unit-of-work behavior without identity tracking.
                // This is generally preferred for event-sourced workloads and stateless APIs.
                .UseLightweightSessions()
                .IntegrateWithWolverine(opts =>
                {
                    // Distribute event subscriptions across nodes and forward fast locally.
                    // Managed distribution avoids duplicate projection work in multi-node setups.
                    opts.UseWolverineManagedEventSubscriptionDistribution = true;
                    // Fast forwarding reduces latency by avoiding intermediate persistence hops.
                    opts.UseFastEventForwarding = true;
                })
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
                    x.PublishEvent<WerewolfTargetLocked>();
                    x.PublishEvent<CupidPairedLovers>();
                    x.PublishEvent<DoctorProtectionChosen>();
                    x.PublishEvent<WitchHealUsed>();
                    x.PublishEvent<WitchPoisonUsed>();
                    x.PublishEvent<WitchPassed>();
                    x.PublishEvent<VoteCast>();
                    x.PublishEvent<GameEnded>();
                    // A Hunter's revenge turn pauses the Night/DayResolution -> next-phase
                    // transition exactly like a night role's turn does, and needs the same
                    // "whose turn is it" push -- all three drive HunterRevengeTurnNotification,
                    // since the queue's head can change on any of them (a new Hunter queued, or
                    // the current one resolving and revealing the next).
                    x.PublishEvent<HunterRevengePending>();
                    x.PublishEvent<HunterRevengeShotFired>();
                    x.PublishEvent<HunterRevengeDeclined>();
                    // Town Square is public, so it's broadcast like any other room-wide event.
                    // PackChatMessageSent is deliberately NOT published here -- see its own
                    // doc comment and GetPackChatEndpoint (poll, not push).
                    x.PublishEvent<RoomChatMessageSent>();
                    // Lobby-side changes push via RoomLobbyViewProjection.RaiseSideEffects instead
                    // (same pattern as GameFlowTriggerProjection) — no separate subscription needed.
                })
                // InitializeWith ensures configured schemas and features are bootstrapped at startup.
                .InitializeWith();

            // Nightly janitor -- see RoomCleanupHostedService's doc comment for the schedule/rules.
            services.AddHostedService<RoomCleanupHostedService>();

            return webApplication;
        }

        private void AddWolverinePlugin()
        {
            webApplication.Host.UseWolverine(opts =>
            {
                // Enable inbox partitioning to reduce contention under concurrent processing.
                // Partitioning spreads inbound message storage across multiple tables/partitions.
                opts.Durability.EnableInboxPartitioning = true;

                // Enable FluentValidation integration for message and HTTP validation.
                // This ensures validators run for handlers and HTTP endpoints automatically.
                opts.UseFluentValidation();
                // Scan the main application assembly for handlers and endpoints.
                // This is the central assembly for commands, queries, and HTTP endpoints.
                opts.ApplicationAssembly = typeof(Program).Assembly;

                // Verbose logging to aid debugging during dev/test; tune as needed.
                // Starting log captures handler entry; execution log captures timing/details.
                opts.Policies.LogMessageStarting(LogLevel.Debug);
                opts.Policies.MessageExecutionLogLevel(LogLevel.Debug);
                // Success log kept at Information to reduce noise while keeping auditability.
                opts.Policies.MessageSuccessLogLevel(LogLevel.Information);

                // Use solo durability for local/integration to reduce infrastructure needs.
                // Solo mode avoids the extra agent infrastructure required for durability.
                if (webApplication.Environment.IsIntegrationOrDevelopment())
                {
                    opts.Durability.Mode = DurabilityMode.Solo;
                }

                // Default policies for local queues and transactional message handling.
                // Durable local queues ensure messages survive process restarts.
                opts.Policies.UseDurableLocalQueues();
                // Auto-apply transactions ties message handling to Marten transaction scope.
                opts.Policies.AutoApplyTransactions();

                // GameFlowTriggerProjection can publish more than one of these for the same room in
                // a single catch-up batch (e.g. Doctor/Seer/Witch acting in quick succession). Both
                // messages route to the same shared local queue regardless of room, so with default
                // parallelism two handler runs can race to FetchLatest + append against the same
                // GameState stream -- the loser hits a Marten version conflict and gets dead-lettered.
                // The handlers are idempotent no-ops when the checklist isn't actually complete, so
                // serializing this one queue (app-wide, not per-room) is enough to remove the race
                // with no correctness cost.
                opts.LocalQueueFor<TryResolveNight>().Sequential();
                opts.LocalQueueFor<TryCloseVoting>().Sequential();

                // Ignore duplicate stream ID collisions to avoid poisoning the pipeline.
                // This is safe when duplicate writes are an expected race and can be discarded.
                opts.OnException<ExistingStreamIdCollisionException>().Discard();

                // SignalR hub for pushing Werewolf game/lobby notifications to connected clients.
                opts.UseSignalR();

                // Without this, UseSignalR() alone registers the transport but nothing tells
                // Wolverine's routing to actually send IGroupWebsocketMessage-implementing
                // messages (e.g. PlayerNotification) out over it.
                opts.Publish(x =>
                {
                    x.MessagesImplementing<IGroupWebsocketMessage>();
                    x.ToSignalR();
                });
            });

            // Configure System.Text.Json for Wolverine and minimal APIs.
            // These settings apply to both HTTP endpoints and message serialization.
            webApplication.Services.ConfigureSystemTextJsonForWolverineOrMinimalApi(o =>
            {
                // Serialize enums as strings and allow case-insensitive JSON property names.
                // String enums are more readable and stable for API consumers.
                o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
                o.SerializerOptions.PropertyNameCaseInsensitive = true;
            });

            // Add Wolverine HTTP endpoints (routing/middleware support).
            // This wires up the HTTP transport and endpoint discovery.
            webApplication.Services.AddWolverineHttp();
        }
    }

    private static MartenServiceCollectionExtensions.MartenConfigurationExpression SetupMartenConfiguration(this IServiceCollection services)
    {
        return services.AddMarten(_ =>
        {
            var opts = new StoreOptions
            {
                Events =
                {
                    // Single tenancy keeps all documents/streams in the default schema.
                    TenancyStyle = TenancyStyle.Single,
                    // Identity map avoids duplicate aggregate loads within a session.
                    UseIdentityMapForAggregates = true,
                    // Quick append favors throughput; appropriate for event streams.
                    AppendMode = EventAppendMode.Quick,
                    // Archive partitioning and mandatory type declarations improve safety.
                    UseArchivedStreamPartitioning = true,
                    // Stream type declarations prevent accidental stream type mismatch.
                    UseMandatoryStreamTypeDeclaration = true
                },
                // Reduce database driver logging noise for production-like runs.
                // This keeps logs focused on application and domain-level events.
                DisableNpgsqlLogging = true
            };

            // Use System.Text.Json for serialization with enum-as-string.
            // JSON settings apply to documents and events stored in Marten.
            opts.UseSystemTextJsonForSerialization(enumStorage: EnumStorage.AsString);
            // Enable common metadata fields on all documents for tracing/auditing.
            // These fields support causal tracing and operational diagnostics.
            opts.Policies.ForAllDocuments(x =>
            {
                x.Metadata.CausationId.Enabled = true;
                x.Metadata.CorrelationId.Enabled = true;
                x.Metadata.Headers.Enabled = true;
                x.Metadata.Version.Enabled = true;
            });
            // Ensure all event metadata fields are enabled for event sourcing diagnostics.
            // This includes causation/correlation, timestamps, and headers.
            opts.Events.MetadataConfig.EnableAll();

            // OpenTelemetry hooks for connections and event counters.
            // Track connections for pool visibility and event counters for throughput.
            opts.OpenTelemetry.TrackConnections = TrackLevel.Normal;
            opts.OpenTelemetry.TrackEventCounters();

            WerewolfMartenModule.Configure(opts);

            return opts;
        });
    }
}

public static class EnvironmentExtensions
{
    extension(IWebHostEnvironment environment)
    {
        public bool IsIntegrationTests() => environment.IsEnvironment("integration");
        public bool IsIntegrationOrDevelopment() => environment.IsIntegrationTests() || environment.IsDevelopment();
        public bool IsAcceptance() => environment.IsEnvironment("acceptance");
    }
}
