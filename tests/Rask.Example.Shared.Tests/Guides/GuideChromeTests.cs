using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Rask.Example.Shared;
using Rask.Example.Shared.Features;
using Rask.Example.Shared.Tests.Infrastructure;

#pragma warning disable RASK014 // test renders the chrome component directly as a root

namespace Rask.Example.Shared.Tests.Guides;

// The narrative-guide chrome: heading extraction (Chapters TOC + on-this-page rail), prev/next reading
// order, and the end-to-end render of a guide with an embedded live demo.
public sealed class GuideChromeTests
{
    [Fact]
    public void Headings_ExtractsH2AndH3_WithStableAnchorIds()
    {
        var headings = Markdown.Headings("# Title\n\n## First section\n\n### A detail\n\n## Second section\n");

        Assert.Collection(headings,
            h => AssertHeading(h, 2, "First section", "first-section"),
            h => AssertHeading(h, 3, "A detail", "a-detail"),
            h => AssertHeading(h, 2, "Second section", "second-section"));
    }

    [Fact]
    public void Headings_SkipsH1AndDeeperThanH3()
    {
        var headings = Markdown.Headings("# H1\n\n## H2\n\n#### H4\n");

        var only = Assert.Single(headings);
        Assert.Equal("H2", only.Text);
    }

    [Fact]
    public void Headings_FlattensInlineCodeInHeadingText()
    {
        var headings = Markdown.Headings("## Programmatic navigation — `Navigator`\n");

        Assert.Equal("Programmatic navigation — Navigator", Assert.Single(headings).Text);
    }

    [Fact]
    public void ReadingOrder_FollowsGroupOrderThenCatalogOrder()
    {
        var order = GuideChrome.ReadingOrder();

        var groups = order.Select(g => Array.IndexOf(GuideCatalog.GroupOrder, g.Group)).ToArray();
        // Group indices are non-decreasing: every guide of an earlier group precedes any of a later one.
        for (var i = 1; i < groups.Length; i++)
        {
            Assert.True(groups[i] >= groups[i - 1],
                "ReadingOrder must be grouped by GuideCatalog.GroupOrder.");
        }

        Assert.Equal(GuideCatalog.All.Length, order.Count);
    }

    [Fact]
    public void Adjacent_FirstGuideHasNoPrev_LastHasNoNext()
    {
        var order = GuideChrome.ReadingOrder();

        var (firstPrev, _) = GuideChrome.Adjacent(order[0].Slug);
        var (_, lastNext) = GuideChrome.Adjacent(order[^1].Slug);

        Assert.Null(firstPrev);
        Assert.Null(lastNext);
    }

    [Fact]
    public void Adjacent_MiddleGuide_LinksBothNeighbours()
    {
        var order = GuideChrome.ReadingOrder();

        var (prev, next) = GuideChrome.Adjacent(order[1].Slug);

        Assert.Equal(order[0].Slug, prev?.Slug);
        Assert.Equal(order[2].Slug, next?.Slug);
    }

    [Fact]
    public void Adjacent_UnknownSlug_YieldsNoNeighbours()
    {
        var (prev, next) = GuideChrome.Adjacent("does-not-exist");

        Assert.Null(prev);
        Assert.Null(next);
    }

    [Fact]
    public void RoutingGuide_RendersChapters_RailPrevNext_AndTheEmbeddedDemo()
    {
        var sp = TestServices.Default();
        var js = sp.GetRequiredService<IJSRuntime>();

        var html = RaskTest.Render(new GuideChrome(js) { Slug = "routing" }, sp).Html;

        // Chrome scaffolding.
        Assert.Contains("guide-chapters", html);
        Assert.Contains("Chapters", html);
        Assert.Contains("guide-onthispage", html);
        Assert.Contains("guide-prevnext", html);
        Assert.Contains("guide-banner", html);

        // The Chapters TOC links a real section anchor (AutoIdentifiers slug of "## Registering routes").
        Assert.Contains("href=\"#registering-routes\"", html);

        // The embedded live demo mounted its CodeSample — the *real* showcase source is shown, proving
        // the marker resolved to a mounted component rather than being dropped as an HTML comment.
        Assert.Contains("guide-demo", html);
        Assert.Contains("RoutingLayoutDemo", html);
        Assert.DoesNotContain("<!-- demo:", html);
        Assert.DoesNotContain("Unknown demo", html);
    }

    [Fact]
    public void FormsGuide_MountsLiveBindingAndValidationDemos()
    {
        var sp = TestServices.Default();
        var js = sp.GetRequiredService<IJSRuntime>();

        var html = RaskTest.Render(new GuideChrome(js) { Slug = "forms" }, sp).Html;

        // Every marker resolved and mounted — no leftover comment, no unknown-demo warning.
        Assert.DoesNotContain("<!-- demo:", html);
        Assert.DoesNotContain("Unknown demo", html);
        // A live binding demo (interactive input + echo) and a validation demo both mounted their
        // CodeSample + result, proving the forms guide is now the destination, not the old /binding page.
        Assert.Contains("guide-demo", html);
        Assert.Contains("sample-result-body", html);
        Assert.Contains("BindingTypedDemo", html);
        Assert.Contains("FluentValidationDemo", html);
    }

    [Fact]
    public void UnknownSlug_RendersNotFound_NotACrash()
    {
        var sp = TestServices.Default();
        var js = sp.GetRequiredService<IJSRuntime>();

        var html = RaskTest.Render(new GuideChrome(js) { Slug = "no-such-guide" }, sp).Html;

        Assert.Contains("No guide found", html);
    }

    private static void AssertHeading(Markdown.Heading h, int level, string text, string id)
    {
        Assert.Equal(level, h.Level);
        Assert.Equal(text, h.Text);
        Assert.Equal(id, h.Id);
    }
}
