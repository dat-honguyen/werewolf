using IntegrationTests.TestHelpers;
using Marten;

namespace IntegrationTests;

public abstract class IntegrationTest(AppFixture appFixture, ITestOutputHelper outputHelper)
    : IntegrationTestBase<AppFixture>(appFixture, outputHelper)
{
    protected IDocumentSession OpenSession()
    {
        var session = AppFixture.OpenSession();
        session.CorrelationId = GetType().Name;
        return session;
    }
}
