using Marten.Events.Daemon;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using System.Threading;
using System.Threading.Tasks;

namespace Application.Infrastructure;

public static class HealthCheckConfiguration
{
    private static readonly string[] PostgresTags = ["postgres"];
    private static readonly string[] AppLifetimeTags = ["applifetime"];

    public static WebApplicationBuilder AddHealthChecksPlugin(this WebApplicationBuilder webApplication)
    {
        webApplication.Services
            .AddSingleton<ApplicationLifetimeHealthCheck>()
            .AddTransient<PostgreSqlHealthCheck>()
            .AddHealthChecks()
            .AddCheck<ApplicationLifetimeHealthCheck>("applifetime", tags: AppLifetimeTags)
            .AddCheck<PostgreSqlHealthCheck>("postgres", tags: PostgresTags)
            .AddMartenAsyncDaemonHealthCheck(maxEventLag: 500);
        return webApplication;
    }
}

public class ApplicationLifetimeHealthCheck : IHealthCheck
{
    private HealthStatus _status = HealthStatus.Unhealthy;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new HealthCheckResult(_status));
    }

    public void SetStatus(HealthStatus status)
    {
        _status = status;
    }
}

public class PostgreSqlHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var connectionString = configuration.GetConnectionString("database");
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand("SELECT 1;", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch
        {
            return HealthCheckResult.Unhealthy();
        }

    }
}
