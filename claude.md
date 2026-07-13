# claude.md

This file shows the requested architecture examples in this sample.

## Environment

- `dotnet` executable path: `/home/dat98/.dotnet/dotnet`

## Reference project

`/home/dat98/s/lion/I-Learn-Mircorservice-and-Event-Sourcing` — another microservice/event-sourcing sample (Marten-based, Application + IntegrationTests layout) to use as an additional example when comparing architecture patterns.

## Controller example

`src/Application/Features/HelloWorld/HelloController.cs`
- `GET /api/v1/hello?name=...`
- `POST /api/v1/hello` -> calls handler and returns `201`
- `GET /api/v1/hello/{id}` -> reads projection

## Handler example

`src/Application/Features/HelloWorld/CreateHelloHandler.cs`
- Accepts `CreateHelloCommand`
- Starts Marten event stream with `HelloCreated`
- Saves and returns `HelloResponse`

## Aggregate example

`src/Application/Features/HelloWorld/HelloAggregate.cs`
- Aggregate state: `Id`, `Name`, `CreatedAt`
- `Create(HelloCreated)` and `Apply(HelloCreated)`

## Projection example

`src/Application/Features/HelloWorld/HelloProjection.cs`
- `SingleStreamProjection<HelloReadModel>`
- `Identity<HelloCreated>(x => x.StreamId)`
- `Create(HelloCreated)` builds read model with hello message

## Multi-stream projection example

`src/Application/Features/HelloWorld/HelloNameStatsProjection.cs`
- `MultiStreamProjection<HelloNameStats, string>`
- `Identity<HelloCreated>(x => x.Name.Trim().ToLowerInvariant())`
- Aggregates events from multiple streams into one stats document per normalized name

## Test example

`src/IntegrationTests/HelloWorld/When_a_user_requests_hello_world.cs`
- Scenario test for GET hello endpoint
- Scenario test for POST + projection readback
- Scenario test for multi-stream stats (`POST` twice + `GET /api/v1/hello/stats/{name}`)

## Single-tenant only

Configured in `src/Application/Infrastructure/CritterConfiguration.cs`:
- No multi-tenant document policy
- No conjoined tenancy setting
- Uses default Marten session (single tenant)
