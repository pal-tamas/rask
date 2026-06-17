using Rask.Core.Routing;
using Rask.Example.Shared.Tests.Infrastructure;

namespace Rask.Example.Shared.Tests.Pages;

public sealed class OrdersPageTests
{
    [Fact]
    public void Route_MasterDetail_RendersOuterGrid()
    {
        var html = Render();

        // Outer grid + its column headers render.
        Assert.Contains("id=\"md-orders\"", html);
        Assert.Contains("Customer", html);
        Assert.Contains("Ada Lovelace", html);
        // Every order row carries a stable key and an addressable expander.
        Assert.Contains("data-rask-key=\"1\"", html);
        Assert.Contains("data-testid=\"expander-1\"", html);
    }

    [Fact]
    public void Default_AllRowsCollapsed_NoInnerGrid()
    {
        var html = Render();

        // Nothing is expanded on first render, so no detail row / inner grid exists yet.
        Assert.DoesNotContain("data-testid=\"inner-", html);
        Assert.DoesNotContain("data-rask-key=\"detail-", html);
    }

    private static string Render() =>
        new Shared.App().RenderAsLiveRoot(
            TestServices.Default(routeState: new RouteState { Path = "/master-detail" }));
}
