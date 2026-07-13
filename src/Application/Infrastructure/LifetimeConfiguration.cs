using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Application.Infrastructure;

public static class LifetimeConfiguration
{
    public static WebApplication RegisterApplicationEvents(this WebApplication webApplication)
    {
        var appLifetime = webApplication.Lifetime;
        var logger = webApplication.Logger;
        var applicationLifetimeCheck = webApplication.Services.GetRequiredService<ApplicationLifetimeHealthCheck>();
        appLifetime.ApplicationStarted.Register(() =>
        {
            applicationLifetimeCheck.SetStatus(HealthStatus.Healthy);
            logger.LogInformation("started");

        });

        appLifetime.ApplicationStopping.Register(() =>
        {
            applicationLifetimeCheck.SetStatus(HealthStatus.Unhealthy);
            logger.LogInformation("stopping");
        });

        appLifetime.ApplicationStopped.Register(() =>
        {
            logger.LogInformation("stopped");
        });
        return webApplication;
    }
}
