using Alba;
using JasperFx.CommandLine;
using JasperFx.Core;
using System.Diagnostics;

namespace IntegrationTests.TestHelpers;

/// <summary>
///     Domain-agnostic base class for the xUnit collection fixture that spins up an in-memory
///     Alba host once per test collection. Adapted from
///     viplive-exploratory-conversation/src/IntegrationTests/TestHelpers/AppFixtureBase.cs -- the
///     LocalStack (S3/SQS) and security-stub helpers from that version were dropped since Werewolf
///     has no AWS transport and no auth.
///
///     Create a derived fixture in your own project (e.g. "AppFixture : AppFixtureBase") and
///     implement <see cref="BuildHostAsync" /> to wire up your own `AlbaHost.For&lt;Program&gt;(...)`
///     and config keys.
/// </summary>
public abstract class AppFixtureBase : IAsyncLifetime
{
    public IAlbaHost Host { get; protected set; } = null!;
    public ITestOutputHelper? OutputHelper { get; set; }

    public virtual async ValueTask InitializeAsync()
    {
        // Workaround for Oakton/JasperFx with WebApplicationBuilder lifecycle issues.
        JasperFxEnvironment.AutoStartHost = true;

        Host = await BuildHostAsync();

        await OnHostReadyAsync();
    }

    public virtual async ValueTask DisposeAsync() => await Host.DisposeAsync();

    /// <summary>
    ///     Implement this in your derived fixture to build the Alba host for your application,
    ///     e.g. via <c>AlbaHost.For&lt;Program&gt;(builder => { ... })</c>.
    /// </summary>
    protected abstract Task<IAlbaHost> BuildHostAsync();

    /// <summary>
    ///     Optional hook invoked right after the host has been created and assigned to
    ///     <see cref="Host" />. Use it for things like applying Marten schema changes or draining
    ///     an async-projection backlog before tests start running.
    /// </summary>
    protected virtual Task OnHostReadyAsync() => Task.CompletedTask;

    public static TimeSpan ProjectionTimeOut() => Debugger.IsAttached ? 60.Seconds() : 10.Seconds();
}
