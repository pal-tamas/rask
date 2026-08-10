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

    // #627. HeadSentinelIndex is an offset into whichever builder is being serialized, and ToHtml()
    // serializes into a private one whose string is handed straight to the caller. A component that calls
    // ToHtml() on a tree containing a <head> — the documented way to demo the document elements, which
    // cannot render live inside a page — used to publish an offset into that private buffer, and the live
    // root then spliced the head-asset block there: into the middle of whatever the real page had at that
    // position, cutting an opening tag in half and losing its attributes.
    //
    // Rendered without an enclosing shell, which is the case that broke: recording is first-wins, so a
    // page with its own <head> was safe by accident while every RaskTest.Render was not.
    // Asserts on the RAW sentinel throughout: the serialized shell that ToHtml() hands back is rendered as
    // text, so the page legitimately contains an HTML-ENCODED copy (&lt;!--__rask_head_assets__--&gt;) —
    // that is the demo showing its own output, not a leak.
    [Fact]
    public void NestedToHtmlOverAHead_DoesNotSpliceIntoTheOuterRender()
    {
        var html = new SerializesAShell().RenderAsLiveRoot();

        // The marker tag must survive intact — the bug cut it after "<span cla" and dropped its class.
        Assert.Contains("<span class=\"marker\"", html);
        Assert.DoesNotContain("<span cla<", html);
    }

    // The counterpart: a page that has its own <head> still splices there, and the nested ToHtml() has not
    // moved the target. Without this, "don't record from ToHtml" could be satisfied by not splicing at all.
    [Fact]
    public void NestedToHtmlOverAHead_LeavesTheRealHeadSpliceAlone()
    {
        var html = new PageShell(new SerializesAShell()).RenderAsLiveRoot();

        // The shell's own sentinel is consumed …
        Assert.DoesNotContain("<!--__rask_head_assets__-->", html);
        // … in <head>, not wherever the nested call happened to point.
        var sentinelWasHere = html.IndexOf("<body", StringComparison.Ordinal);
        Assert.True(sentinelWasHere > 0);
        Assert.Contains("<span class=\"marker\"", html);
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
            Doctype,
            Html.Lang("en")[
                // Head() is framework-managed: the serializer auto-inserts the
                // head-asset sentinel inside, so contributions splice in without
                // any explicit placeholder.
                Head(),
                Body[_body]
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

        protected override Component? Head => Title[_title];

        protected override Component? Render() =>
        [
            Doctype,
            Html.Lang("en")[
                // Head() is framework-managed: the serializer auto-inserts the
                // head-asset sentinel inside, so contributions splice in without
                // any explicit placeholder.
                Head(),
                Body[_body]
            ]
        ];
    }

    private sealed class NoHeadComponent : Component
    {
        protected override Component? Render() => Div["plain body"];
    }

    // Mirrors ElementsMetadataDemo: composes a real document shell and shows its serialized HTML as text.
    // The <span class="marker"> after it is what the misplaced splice used to cut in half.
#pragma warning disable RASK019 // composing Head() children directly is the point here
    private sealed class SerializesAShell : Component
    {
        protected override Component? Render() =>
        [
            Pre[Code[Html.Lang("en")[Head()[Title["Inner"]], Body[P["hi"]]].ToHtml()]],
            Span.Class("marker")["after"]
        ];
    }
#pragma warning restore RASK019

    private sealed class ContributesLink : Component
    {
        protected override Component? Head => Link.Rel("stylesheet").Href("/a.css");
        protected override Component? Render() => Div["with link"];
    }

    private sealed class ContributesTitle : Component
    {
        private readonly string _title;
        public ContributesTitle(string title) => _title = title;
        protected override Component? Head => Title[_title];
        protected override Component? Render() => Div["with title"];
    }

    private sealed class ContributesTitleWithId : Component
    {
        public int Id { get; set; }
        protected override Component? Head => Title[$"User #{Id}"];
        protected override Component? Render() => Div[$"user {Id}"];
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

        protected override Component? Render() => Div[_a, _b];
    }
}
