using Rask.Core.Routing;

// RASK014: the router is a ROOT's only child, built here rather than by a chain because nothing renders this
// file's code as markup. One instance, captured, so every render hands back the same router — a new one each
// render would remount the page and lose its state.
#pragma warning disable RASK014

namespace Rask.Testing;

public static partial class Test
{
    /// <summary>
    ///     Opens the app at <paramref name="url" />, through the real router: the page registered for that URL
    ///     renders, its route and query parameters bound, and a click that navigates moves to the next page —
    ///     the way a person uses it.
    /// </summary>
    /// <remarks>
    ///     <code>
    ///     var page = Test.Visit("/products/new");
    ///
    ///     await page.Type("Tea").Into("Name");
    ///     await page.Click("Save");
    ///
    ///     page.Shows("Product saved");
    ///     page.IsAt("/products");
    ///     </code>
    ///     <paramref name="services" /> are the app's own — the router adds the page's route and navigator over them.
    /// </remarks>
    public static Page Visit(string url, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(url);
        var route = TestRoute.At(url);
        var routed = new RoutedServices(route, TestRoute.NavigatorFor(route), services);
        // Routes = null resolves to the app's registered route table, which the chain entry does for an app.
        var router = new Router(route) { Routes = null };
        return Render(() => router, routed);
    }

    // The page's own route and navigator first, then whatever the test brought.
    private sealed class RoutedServices(RouteState route, Navigator navigator, IServiceProvider? app) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(RouteState) ? route
            : serviceType == typeof(Navigator) ? navigator
            : app?.GetService(serviceType);
    }
}
