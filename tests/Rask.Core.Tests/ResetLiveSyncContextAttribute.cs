using System.Reflection;
using Rask.Core.Live;
using Xunit.Sdk;

// Applies to every test in this assembly: guarantees a clean LiveRenderContext.CurrentSync
// before each test body runs. The thread-static sync mirror can linger on a pooled thread after
// an async render released it at an await (see LiveRenderContext.CurrentSync); xUnit then reuses
// that thread for a synchronous test asserting "no live context". Resetting before every test
// makes those assertions deterministic. Before() runs on the test's own thread, before the body.
[assembly: Rask.Core.Tests.ResetLiveSyncContext]

namespace Rask.Core.Tests;

public sealed class ResetLiveSyncContextAttribute : BeforeAfterTestAttribute
{
    public override void Before(MethodInfo methodUnderTest) => LiveRenderContext.ResetSyncForTests();
}
