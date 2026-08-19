using Rask.Core.Routing;
using Rask.Dashboard.Pages;

namespace Rask.Dashboard.Tests;

/// <summary>
///     The dashboard now lives under <c>/_rask</c>, which is also the framework's own reserved prefix:
///     scoped assets are served from <c>/_rask/a/{hash}.{ext}</c>, and the live runtime owns
///     <c>/_rask/auth/redeem</c>, <c>/_rask/upload/{sessionId}</c> and
///     <c>/_rask/download/{sessionId}/{token}</c>.
///     <para>
///         Those are literal endpoints and the dashboard's pages resolve through a catch-all, so routing
///         precedence already keeps the framework's paths working — a page named <c>auth</c> would not
///         break asset serving. The damage runs the other way: the page would simply never be reachable,
///         because the literal endpoint wins every request for it. Nothing would fail at build or at
///         startup; the tab would just 404 in a way that looks like a routing bug in the app.
///     </para>
///     <para>
///         One assertion, checked against the framework's list rather than a copy of it, so adding a
///         dashboard page called <c>upload</c> fails here instead of in someone's browser.
///     </para>
/// </summary>
public class ReservedPrefixTests
{
    /// <summary>
    ///     The first segment under <c>/_rask</c> that each framework endpoint claims. Mirrored from
    ///     <c>Rask.Server.RaskEndpointExtensions</c> and <c>Rask.Wasm.Hosting.RaskAssetEndpoint</c>, which
    ///     this assembly cannot reference — the dashboard deliberately takes no host dependency.
    /// </summary>
    private static readonly string[] _frameworkOwned = ["a", "auth", "upload", "download"];

    [Fact]
    public void No_dashboard_page_shadows_a_framework_owned_segment()
    {
        Assert.NotNull(typeof(DashboardLayout).FullName);

        var layout = RouteRegistry.BuildTree().FirstOrDefault(r => r.PageType == typeof(DashboardLayout));
        Assert.NotNull(layout);
        Assert.Equal("_rask", layout!.Template);

        foreach (var child in layout.SubRoutes ?? [])
        {
            // The page's own first segment, before any route parameter — "queues/{queue}" is "queues".
            var first = child.Template.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (first is null)
            {
                continue; // the overview page, which is the prefix itself
            }

            Assert.False(
                _frameworkOwned.Contains(first, StringComparer.Ordinal),
                $"Dashboard page {child.PageType.Name} routes to '/_rask/{child.Template}', but the "
                + $"framework already serves '/_rask/{first}/...' as a literal endpoint. That endpoint "
                + "outranks the dashboard's catch-all, so this page would silently 404. Rename it.");
        }
    }
}
