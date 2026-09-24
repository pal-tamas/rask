using Rask.Site.Features;
using Rask.Site.Tests.Infrastructure;

namespace Rask.Site.Tests.Pages;

// MasterDetailDemo (embedded in the Composition guide's "Keyed lists" section) is the former
// /master-detail page with the page chrome stripped — the outer grid, keyed detail rows, and both
// sorts are unchanged. Rendered directly here since its standalone page was folded into the guide.
public sealed class MasterDetailDemoTests
{
    [Fact]
    public void The_demo_renders_the_outer_grid_with_keyed_rows_and_expanders()
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
    public void By_default_all_rows_are_collapsed_with_no_inner_grid()
    {
        var html = Render();

        // Nothing is expanded on first render, so no detail row / inner grid exists yet.
        Assert.DoesNotContain("data-testid=\"inner-", html);
        Assert.DoesNotContain("data-rask-key=\"detail-", html);
    }

    private static string Render() =>
        Page.Render(new MasterDetailDemo(), TestServices.Default()).Html;
}
