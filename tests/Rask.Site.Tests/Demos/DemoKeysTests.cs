using System.Collections.Concurrent;
using Rask.Core.Diagnostics;
using Rask.Core.Live;
using Rask.Site.Features;
using Rask.Site.Tests.Infrastructure;

#pragma warning disable RASK014 // the App is rendered directly as a root

namespace Rask.Site.Tests.Demos;

// A component's Key names ONE component of its type among everything its owner writes. A demo that writes
// one twice still draws, and the runtime says so once, in a warning nobody reads in a publish log — while
// the demos are what readers copy. So every page the publish prerenders and every registered demo is
// rendered here, and that warning fails the build's tests.
public sealed partial class DemoKeysTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task No_page_or_demo_writes_one_key_twice_for_a_type()
    {
        _ = typeof(PwaPage);
        var paths = RaskPrerender.PlanRoutes().Paths.Concat(new GuidePrerenderPaths().Paths()).Distinct(StringComparer.Ordinal);
        var repeated = new ConcurrentQueue<string>();
        var previous = RaskDiagnostics.Sink;
        RaskDiagnostics.Sink = e =>
        {
            if (e.Message.Contains("components with Key", StringComparison.Ordinal))
            {
                repeated.Enqueue(e.Message[..e.Message.IndexOf(" in one render", StringComparison.Ordinal)]);
            }
        };

        try
        {
            // The runtime reports a repeated key once per process; what an earlier test rendered counts again.
            RaskDiagnostics.ResetReportOnceForTests();
            foreach (var path in paths)
            {
                await RaskPrerender.RenderDocument(
                    new global::Rask.Site.App(), TestServices.Default(routeState: TestRouteState.At(path)), TimeSpan.FromSeconds(10),
                    cancellationToken: TestContext.Current.CancellationToken);
            }

            foreach (var key in DemoRegistry.Keys)
            {
                _ = Page.Render(() => DemoRegistry.Build(key), TestServices.Default()).Html;
            }
        }
        finally
        {
            RaskDiagnostics.Sink = previous;
        }

        Assert.True(repeated.IsEmpty, "Give each a Key of its own:\n  " + string.Join("\n  ", repeated));
    }
}
