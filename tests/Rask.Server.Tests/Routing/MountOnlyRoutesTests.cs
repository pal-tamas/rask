using System.Runtime.CompilerServices;
using Rask.Core.Routing;
using Rask.Dashboard;

namespace Rask.Server.Tests.Routing;

public sealed class MountOnlyRoutesTests
{
    [Fact]
    public void The_dashboard_s_pages_are_served_only_where_it_is_mounted()
    {
        // Rask.Server carries Rask.Dashboard, so every server app in the process has its route registry
        // loaded — including an app that never called AddRaskDashboard. Its pages must not be in that app's
        // table: /_rask/… would route to a page whose authorization policy does not exist and answer 500.
        var dashboard = typeof(RaskDashboardShell).Assembly;
        RuntimeHelpers.RunModuleConstructor(dashboard.ManifestModule.ModuleHandle);

        var main = Pages(RouteRegistry.BuildTree());
        var mounted = Pages(RouteRegistry.BuildTree(dashboard));

        Assert.DoesNotContain(main, page => page.Assembly == dashboard);
        Assert.Contains(mounted, page => page.Assembly == dashboard);
    }

    private static List<Type> Pages(IReadOnlyList<Route> routes)
    {
        var pages = new List<Type>();
        Walk(routes);
        return pages;

        void Walk(IReadOnlyList<Route> level)
        {
            foreach (var route in level)
            {
                pages.Add(route.PageType);
                Walk(route.SubRoutes ?? []);
            }
        }
    }
}
