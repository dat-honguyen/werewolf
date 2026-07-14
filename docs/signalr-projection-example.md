# SignalR projection side-effect example

This shows a generic Wolverine + Marten + SignalR setup where a projection publishes a group message after it updates a read model.

## Program configuration

```csharp
using MyApp.Infrastructure.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder
    .AddSignalR()
    .AddCritterPlugin();

var app = builder.Build();

app.MapWolverineSignalRHub("/api/messages")
   .RequireAuthorization();

app.Run();
```

```csharp
using StackExchange.Redis;

namespace MyApp.Infrastructure.SignalR;

public static class SignalRConfiguration
{
    public static WebApplicationBuilder AddSignalR(this WebApplicationBuilder builder)
    {
        var settings = builder.Configuration.GetSection(SignalRSettings.SECTION).Get<SignalRSettings>();
        if (settings is null || !settings.UseRedisBackplane)
        {
            return builder;
        }

        builder.Services
            .AddSignalR()
            .AddStackExchangeRedis(settings.RedisConnectionString, options =>
            {
                options.Configuration.ChannelPrefix = RedisChannel.Literal("myapp");
            });

        return builder;
    }
}

public class SignalRSettings
{
    public const string SECTION = "SignalR";
    public required bool UseRedisBackplane { get; init; }
    public required string RedisConnectionString { get; init; }
}
```

```json
{
  "SignalR": {
    "UseRedisBackplane": true,
    "RedisConnectionString": "localhost:6379"
  }
}
```

## Group message handling

```csharp
using Wolverine.SignalR;

namespace MyApp.Infrastructure.SignalR.Wolverine;

public static class GroupHandler
{
    public static AddConnectionToGroup Handle(JoinProjectGroup message)
        => new(message.GroupName);

    public static RemoveConnectionToGroup Handle(LeaveProjectGroup message)
        => new(message.GroupName);
}

public record JoinProjectGroup(string GroupName) : IGroupWebsocketMessage;
public record LeaveProjectGroup(string GroupName) : IGroupWebsocketMessage;

public interface IGroupWebsocketMessage : WebSocketMessage;
```

```csharp
using Wolverine.SignalR;

namespace MyApp.Infrastructure.SignalR.Wolverine;

public static class GroupNotificationHandler
{
    public static SignalRMessage<ProjectUpdated> Handle(NotifyProjectGroupUpdated message)
        => new ProjectUpdated().ToWebSocketGroup(message.GroupName);
}

public record NotifyProjectGroupUpdated(string GroupName) : IGroupWebsocketMessage;
public record ProjectUpdated : IGroupWebsocketMessage;
```

## Projection with side effects

```csharp
using Marten.Events.Aggregation;
using MyApp.Infrastructure.SignalR.Wolverine;

namespace MyApp.Features.Projects;

public class ProjectProjection : SingleStreamProjection<ProjectView, string>
{
    public static ProjectView Create(IEvent<ProjectCreated> @event) =>
        new()
        {
            Id = @event.StreamKey!,
            Name = @event.Data.Name,
            Status = @event.Data.Status
        };

    public static ProjectView Apply(ProjectRenamed renamed, ProjectView view) =>
        view with { Name = renamed.Name };

    public override ValueTask RaiseSideEffects(IDocumentOperations operations, IEventSlice<ProjectView> slice)
    {
        var group = slice.Snapshot?.Id;
        if (group is not null)
        {
            slice.PublishMessage(new NotifyProjectGroupUpdated(group));
        }

        return ValueTask.CompletedTask;
    }
}

public record ProjectView
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Status { get; init; }
}
```

## Wolverine SignalR wiring

```csharp
opts.UseSignalR();

opts.Publish(x =>
{
    x.MessagesImplementing<IGroupWebsocketMessage>();
    x.ToSignalR();
});
```

## Flow

1. The projection updates the read model.
2. `RaiseSideEffects()` publishes a Wolverine message.
3. Wolverine maps that message to a SignalR group message.
4. Connected clients in that group receive the update and can refresh their data.
