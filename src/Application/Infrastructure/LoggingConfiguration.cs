using Microsoft.AspNetCore.Builder;
using Serilog;

namespace Application.Infrastructure;

public static class LoggingConfiguration
{
    public static WebApplicationBuilder AddLoggingPlugin(this WebApplicationBuilder webApplication)
    {
        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(webApplication.Configuration)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithEnvironmentName()
            .Enrich.WithThreadId()
            .CreateLogger();

        webApplication.Host.UseSerilog();

        return webApplication;
    }
}
