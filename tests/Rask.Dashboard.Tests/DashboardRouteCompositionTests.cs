using Rask.Core.Routing;
using Rask.Dashboard.Pages;

namespace Rask.Dashboard.Tests;

/// <summary>
///     The dashboard's pages compose onto the layout's <c>_ops</c> prefix through <c>Page.Parent</c>. If that
///     link ever failed to resolve, each page would register at the top level instead and every dashboard URL
///     would 404 — which, behind the layout's [Authorize], surfaces as a redirect to sign-in rather than as a
///     missing page. Cheap to assert, and the symptom is misleading enough to be worth pinning.
/// </summary>
public class DashboardRouteCompositionTests
{
    [Fact]
    public void EveryPage_HangsOffTheLayout()
    {
        // Touch a dashboard type first so its assembly (and therefore its generated route-registry module
        // initializer) is definitely loaded before the tree is built.
        Assert.NotNull(typeof(DashboardLayout).FullName);

        var tree = RouteRegistry.BuildTree();
        var layout = tree.FirstOrDefault(r => r.PageType == typeof(DashboardLayout));

        Assert.True(layout is not null,
            "no DashboardLayout at the top of the tree; got: "
            + string.Join(", ", tree.Select(r => $"{r.PageType.Name}@'{r.Template}'")));
        Assert.Equal("_ops", layout!.Template);

        var children = layout.SubRoutes ?? [];
        Assert.Contains(children, c => c.PageType == typeof(LogsPage) && c.Template == "logs");
        Assert.Contains(children, c => c.PageType == typeof(OverviewPage));
        Assert.Contains(children, c => c.PageType == typeof(QueuePage));
    }
}
