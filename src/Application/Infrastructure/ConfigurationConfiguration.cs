using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace Application.Infrastructure;

public static class ConfigurationConfiguration
{
    public static WebApplicationBuilder AddConfigurationPlugin(this WebApplicationBuilder webApplication)
    {
        webApplication.Configuration
            .AddUserSecrets(typeof(Program).Assembly, true)
            .AddJsonFile("appsettings.json", false)
            .AddJsonFile($"appsettings.{webApplication.Environment.EnvironmentName}.json", true)
            .AddEnvironmentVariables("TDIL_"); // prefix
        return webApplication;
    }
}
