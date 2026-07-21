namespace IntegrationTests.TestHelpers;

/// <summary>
///     Integration tests run against the same `werewolf` Postgres database docker-compose.yml
///     provisions for local dev (see run-werewolf skill) -- Program.cs's own appsettings.json
///     already points there by default, so nothing needs overriding for local runs. Isolation from
///     dev/manual traffic comes from a dedicated Marten schema (see AppFixture.SchemaName), not a
///     separate database.
///
///     Set WEREWOLF_TEST_DB (e.g. for CI, where Postgres isn't at localhost) to override the
///     connection string instead.
/// </summary>
public static class ConnectionSource
{
    public static readonly string? ConnectionStringOverride = Environment.GetEnvironmentVariable("WEREWOLF_TEST_DB");
}
