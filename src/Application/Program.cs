using Application.Infrastructure;
using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);

builder
    .AddConfigurationPlugin()
    .AddLoggingPlugin()
    .AddCritterPlugin()
    .AddHealthChecksPlugin()
    .AddEndpointsModule()
    .AddForwardHeadersConfigs();

var app = builder.Build();

app.RegisterApplicationEvents();
app.ConfigureEndpointsModule();

return await app.RunJasperFxCommands(args);

namespace Application
{
    public partial class Program;
}
