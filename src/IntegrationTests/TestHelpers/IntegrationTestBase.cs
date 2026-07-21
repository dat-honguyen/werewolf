using Alba;
using System.Diagnostics;
using Wolverine.Tracking;

namespace IntegrationTests.TestHelpers;

/// <summary>
///     Domain-agnostic base class for integration tests: Alba HTTP scenarios plus Wolverine
///     tracked message sends. Adapted from
///     viplive-exploratory-conversation/src/IntegrationTests/TestHelpers/IntegrationTestBase.cs --
///     the LocalStack (S3/SQS) and Mailpit helpers from that version were dropped since Werewolf
///     doesn't use AWS or email.
/// </summary>
/// <typeparam name="TFixture">Your project's derived <see cref="AppFixtureBase" /> type.</typeparam>
public abstract class IntegrationTestBase<TFixture>
    where TFixture : AppFixtureBase
{
    protected readonly TFixture AppFixture;

    protected IntegrationTestBase(TFixture appFixture, ITestOutputHelper outputHelper)
    {
        AppFixture = appFixture;
        AppFixture.OutputHelper = outputHelper;
        Host = AppFixture.Host;

        OnInitialize();
    }

    protected IAlbaHost Host { get; }

    /// <summary>
    ///     Override to run any per-test setup that is specific to your application.
    /// </summary>
    protected virtual void OnInitialize()
    {
    }

    /// <summary>
    ///     This method allows us to make HTTP calls into our system in memory with Alba, but do so
    ///     within Wolverine's test support for message tracking to both record outgoing messages
    ///     and to ensure that any cascaded work spawned by the initial command is completed before
    ///     passing control back to the calling test.
    /// </summary>
    protected async Task<(ITrackedSession tracked, IScenarioResult? result)> TrackedHttpCall(Action<Scenario> configuration, int timeoutInMilliseconds = 10_000)
    {
        IScenarioResult? result = null;

        var tracked = await Host
            .ExecuteAndWaitAsync(async () => { result = await Host.Scenario(configuration); }, SessionsTimeOut(timeoutInMilliseconds));

        return (tracked, result);
    }

    protected static TimeSpan ProjectionTimeOut() => AppFixtureBase.ProjectionTimeOut();
    protected static TimeSpan ProjectionTimeOut(TimeSpan timeOut) => Debugger.IsAttached ? TimeSpan.FromSeconds(60) : timeOut;

    protected static int SessionsTimeOut(int timeoutInMilliseconds = 10_000) => Debugger.IsAttached ? 60_000 : timeoutInMilliseconds;
}
