using System.Text.Json;
using Rask.Example.Shared.Tests.Infrastructure;
using static Rask.Example.Shared.Features.Generated;

namespace Rask.Example.Shared.Tests.Demos;

// ComponentTiersDemo (embedded in the Composition guide's "Component tiers" section) shows the three
// ways to author a reusable unit side by side: a Tier-0 static helper, a Tier-1 stateless component,
// and a Tier-2 stateful counter. These pin each tier's render, plus the defining Tier-2 behaviour —
// clicking the button mutates a private field and re-renders with NO StateHasChanged() call, driven
// through the same handler-dispatch path the live runtime uses.
public sealed class ComponentTiersDemoTests
{
    [Fact]
    public void Render_ShowsAllThreeTiers()
    {
        var page = RaskTest.Render(() => ComponentTiersDemo(), TestServices.Default());
        var html = page.Render();

        // Tier 0 — the static helper's inlined badges.
        Assert.Contains("inlined", html);
        Assert.Contains("no lifecycle", html);
        // Tier 1 — the stateless greeting renders purely from its prop.
        Assert.Contains("Hello, ", html);
        Assert.Contains("Ada", html);
        // Tier 2 — the stateful counter starts at zero.
        Assert.Contains("Clicked 0 times", html);
    }

    [Fact]
    public async Task StatefulCounter_Click_Increments_WithoutStateHasChanged()
    {
        var page = RaskTest.Render(() => ComponentTiersDemo(), TestServices.Default());
        var clickId = ClickHandler(page.Render());

        await page.InvokeAsync(clickId);
        await page.InvokeAsync(clickId);

        var final = page.Render();
        Assert.Contains("Clicked 2 times", final);
        Assert.DoesNotContain("Clicked 0 times", final);
    }

    // The counter's OnClick is the only wired event in the demo, so its id is the first (and only)
    // data-rask-on-click attribute in the rendered HTML.
    private static string ClickHandler(string html)
    {
        const string marker = "data-rask-on-click=\"";
        var i = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(i >= 0, "no click handler rendered for the stateful counter");
        i += marker.Length;
        return html[i..html.IndexOf('"', i)];
    }
}
