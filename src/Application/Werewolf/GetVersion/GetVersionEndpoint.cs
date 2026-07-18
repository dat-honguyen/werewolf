namespace Application.Werewolf.GetVersion;

public record VersionResponse
{
    public required string Version { get; init; }
}

// Reference endpoint reporting the running build's version, sourced from the APP_VERSION env
// var baked into the Docker image at build time (the git release tag, or "dev-<short-sha>" for
// non-release builds). Lets a client display which backend build it's actually talking to.
public static class GetVersionEndpoint
{
    [WolverineGet("/api/v1/version")]
    public static VersionResponse Handle() => new()
    {
        Version = Environment.GetEnvironmentVariable("APP_VERSION") ?? "dev"
    };
}
