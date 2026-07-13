using Application.Infrastructure;
using Application.Werewolf;
using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);

builder
    .AddConfigurationPlugin()
    .AddLoggingPlugin()
    .AddCritterPlugin()
    .AddWerewolf()
    .AddHealthChecksPlugin()
    .AddEndpointsModule()
    .AddForwardHeadersConfigs();

var app = builder.Build();

app.RegisterApplicationEvents();
app.ConfigureEndpointsModule();

app.Run();

namespace Application
{
    public partial class Program;
}
