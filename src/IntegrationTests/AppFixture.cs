using Alba;
using Application;
using IntegrationTests.TestHelpers;
using Marten;
using Marten.Events;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace IntegrationTests;

public class AppFixture : AppFixtureBase
{
    // Isolates integration-test data from local dev/manual traffic without needing a separate
    // Postgres database -- both live in `werewolf`, just under different Marten schemas.
    private const string SchemaName = "integration_tests";

    public IDocumentStore Store => Host.Services.GetRequiredService<IDocumentStore>();

    protected override async Task<IAlbaHost> BuildHostAsync()
    {
        if (ConnectionSource.ConnectionStringOverride is { } connectionString)
        {
            // Program.cs's own AddConfigurationPlugin() chain (appsettings.json -> appsettings.
            // {EnvironmentName}.json -> env vars prefixed "TDIL_") is applied *after* Alba's
            // ConfigureAppConfiguration callback, so a ConfigureAppConfiguration override here
            // would get silently clobbered by appsettings.json's own ConnectionStrings:database --
            // confirmed empirically. Env vars are the one link in that chain applied last.
            Environment.SetEnvironmentVariable("TDIL_ConnectionStrings__database", connectionString);
        }

        return await AlbaHost.For<Program>(b =>
        {
            b.UseEnvironment("Integration");
            b.ConfigureServices((_, services) => services.AddSingleton<IConfigureMarten>(new TestSchemaConfigurer(SchemaName)));
        });
    }

    protected override async Task OnHostReadyAsync()
    {
        await Store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await Host.ForceAllMartenDaemonActivityToCatchUpAsync(TestContext.Current.CancellationToken);
    }

    public IDocumentSession OpenSession() => Store.LightweightSession();

    private sealed class TestSchemaConfigurer(string schemaName) : IConfigureMarten
    {
        public void Configure(IServiceProvider services, StoreOptions options)
        {
            options.DatabaseSchemaName = schemaName;
            options.Events.DatabaseSchemaName = schemaName;
        }
    }
}
