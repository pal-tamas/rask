#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.HeadAssets;

// End-to-end render tests for Component.Head + framework-managed <head> — exercises
// the HtmlSerializer.Add → HeadAssetRegistry → Component.RenderAsLiveRootCore splice
// pass. Unit-level dedup behavior lives in HeadAssetRegistryTests; this file
// asserts the rendered HTML you'd actually see in the browser.
public class HeadAssetRenderTests
{
    [Fact]
    public void NoHeadContribution_SentinelStrippedFromOutput()
    {
        var view = new PageShell(new NoHeadComponent());
        var html = view.RenderAsLiveRoot();
        Assert.DoesNotContain("__rask_head_assets__", html);
    }

    [Fact]
    public void SingleHead_AssetSplicedAtSentinelPosition()
    {
        var view = new PageShell(new ContributesLink());
        var html = view.RenderAsLiveRoot();

        Assert.DoesNotContain("__rask_head_assets__", html);
        Assert.Contains("href=\"/a.css\"", html);
        // Spliced into <head>, before the body opens.
        var assetIdx = html.IndexOf("/a.css", StringComparison.Ordinal);
        var bodyIdx = html.IndexOf("<body", StringComparison.Ordinal);
        Assert.True(assetIdx > 0 && bodyIdx > 0 && assetIdx < bodyIdx);
    }

    [Fact]
    public void DuplicateContributions_DedupToSingleEmission()
    {
        // Two children both contributing the same <link> should produce exactly one
        // <link> in head — that's the whole point of dedup-by-rendered-HTML.
        var view = new PageShell(new TwoChildHost(new ContributesLink(), new ContributesLink()));
        var html = view.RenderAsLiveRoot();

        Assert.Equal(1, CountOccurrences(html, "/a.css"));
    }

    [Fact]
    public void TitleSingleton_RootContributorOverriddenByChild()
    {
        // Root contributes Title="App". Component contributes Title="Page". The page wins —
        // exactly one <title> in head, content is "Page". Exercises the singleton dedup
        // through the full render path, not just the registry in isolation.
        var view = new ShellWithTitle("App", new ContributesTitle("Page"));
        var html = view.RenderAsLiveRoot();

        Assert.Equal(1, CountOccurrences(html, "<title "));
        Assert.Contains(">Page</title>", html);
        Assert.DoesNotContain(">App</title>", html);
    }

    [Fact]
    public void TitleSingleton_RootOnly_RootTitleApplies()
    {
        // Root contributes Title="App"; child has no Head. The App's title is the only
        // <title> in head — the fallback semantic that lets an app set a default that
        // pages can override.
        var view = new ShellWithTitle("App", new NoHeadComponent());
        var html = view.RenderAsLiveRoot();

        Assert.Equal(1, CountOccurrences(html, "<title "));
        Assert.Contains(">App</title>", html);
    }

    [Fact]
    public void InterpolatedHead_PicksUpInstanceState()
    {
        // Head is a property getter on the instance — it can interpolate fields the
        // same way Render() can. Mirrors the UserDetailPage pattern (Title with
        // RouteParam-bound id).
        var view = new PageShell(new ContributesTitleWithId { Id = 42 });
        var html = view.RenderAsLiveRoot();
        Assert.Contains(">User #42</title>", html);
    }

    [Fact]
    public void RepeatedRenders_ProduceIdenticalHead()
    {
        // The head-asset registry + mounted-type set are hoisted onto the root's LiveState and
        // reused across renders (cleared each frame). Re-rendering the same root must yield the
        // exact same head every time — a stale entry surviving Clear() (dup link, leftover
        // title) would show up here. Also guards the singleton/dedup paths under reuse.
        var view = new ShellWithTitle("App", new ContributesLink());

        var first = view.RenderAsLiveRoot();
        var second = view.RenderAsLiveRoot();
        var third = view.RenderAsLiveRoot();

        Assert.Equal(first, second);
        Assert.Equal(second, third);
        Assert.Equal(1, CountOccurrences(third, "<title "));
        Assert.Equal(1, CountOccurrences(third, "/a.css"));
    }

    // ----- helpers -----

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }

        return count;
    }

    private sealed class PageShell : Component
    {
        private readonly Component _body;
        public PageShell(Component body) => _body = body;

        protected override Component? Render() =>
        [
            Doctype(),
            Html("en")[
                // Head() is framework-managed: the serializer auto-inserts the
                // head-asset sentinel inside, so contributions splice in without
                // any explicit placeholder.
                Head(),
                Body()[_body]
            ]
        ];
    }

    private sealed class ShellWithTitle : Component
    {
        private readonly Component _body;
        private readonly string _title;

        public ShellWithTitle(string title, Component body)
        {
            _title = title;
            _body = body;
        }

        protected override Component? Head => Title()[_title];

        protected override Component? Render() =>
        [
            Doctype(),
            Html("en")[
                // Head() is framework-managed: the serializer auto-inserts the
                // head-asset sentinel inside, so contributions splice in without
                // any explicit placeholder.
                Head(),
                Body()[_body]
            ]
        ];
    }

    private sealed class NoHeadComponent : Component
    {
        protected override Component? Render() => Div()["plain body"];
    }

    private sealed class ContributesLink : Component
    {
        protected override Component? Head => Link(Rel: "stylesheet", Href: "/a.css");
        protected override Component? Render() => Div()["with link"];
    }

    private sealed class ContributesTitle : Component
    {
        private readonly string _title;
        public ContributesTitle(string title) => _title = title;
        protected override Component? Head => Title()[_title];
        protected override Component? Render() => Div()["with title"];
    }

    private sealed class ContributesTitleWithId : Component
    {
        public int Id { get; set; }
        protected override Component? Head => Title()[$"User #{Id}"];
        protected override Component? Render() => Div()[$"user {Id}"];
    }

    private sealed class TwoChildHost : Component
    {
        private readonly Component _a;
        private readonly Component _b;

        public TwoChildHost(Component a, Component b)
        {
            _a = a;
            _b = b;
        }

        protected override Component? Render() => Div()[_a, _b];
    }
}
