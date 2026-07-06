#pragma warning disable RASK014 // private test Component subclass — no generated factory needed

using System.Text;
using Rask.Core.HeadAssets;

namespace Rask.Core.Tests.HeadAssets;

public class HeadAssetRegistryTests
{
    [Fact]
    public void Add_DedupsIdenticalHtml()
    {
        var registry = new HeadAssetRegistry();
        registry.Add(Link(Rel: "stylesheet", Href: "/a.css"));
        registry.Add(Link(Rel: "stylesheet", Href: "/a.css"));

        var html = $"<head>{HeadAssetRegistry.Sentinel}</head>";
        var result = registry.ApplyTo(html);

        // Two adds, one survives (same rendered HTML).
        Assert.Equal(1, CountOccurrences(result, "href=\"/a.css\""));
    }

    [Fact]
    public void Add_KeepsDistinctHtmlInOrder()
    {
        var registry = new HeadAssetRegistry();
        registry.Add(Link(Rel: "stylesheet", Href: "/a.css"));
        registry.Add(Link(Rel: "stylesheet", Href: "/b.css"));

        var html = $"<head>{HeadAssetRegistry.Sentinel}</head>";
        var result = registry.ApplyTo(html);

        var aIdx = result.IndexOf("/a.css", StringComparison.Ordinal);
        var bIdx = result.IndexOf("/b.css", StringComparison.Ordinal);
        Assert.True(aIdx > 0 && bIdx > 0);
        Assert.True(aIdx < bIdx);
    }

    [Fact]
    public void Add_TitleIsSingleton_LastWins()
    {
        var registry = new HeadAssetRegistry();
        registry.Add(Title()["App default"]);
        registry.Add(Title()["Page override"]);

        var html = $"<head>{HeadAssetRegistry.Sentinel}</head>";
        var result = registry.ApplyTo(html);

        Assert.Equal(1, CountOccurrences(result, "<title "));
        Assert.Contains("Page override", result);
        Assert.DoesNotContain("App default", result);
    }

    [Fact]
    public void Add_TitleWithAttributes_IsStillSingleton()
    {
        var registry = new HeadAssetRegistry();
        // The HTML spec doesn't allow arbitrary attrs on <title>, but the dedup logic
        // shouldn't be fooled by a leading-space attribute case either.
        registry.Add(Title()["First"]);
        registry.Add(Title()["Second"]);
        var html = $"<head>{HeadAssetRegistry.Sentinel}</head>";
        var result = registry.ApplyTo(html);
        Assert.Equal(1, CountOccurrences(result, "<title "));
        Assert.Contains("Second", result);
    }

    [Fact]
    public void Add_BaseTagIsSingleton_LastWins()
    {
        var registry = new HeadAssetRegistry();
        registry.Add(Base("/old/"));
        registry.Add(Base("/new/"));

        var html = $"<head>{HeadAssetRegistry.Sentinel}</head>";
        var result = registry.ApplyTo(html);

        Assert.Equal(1, CountOccurrences(result, "<base "));
        Assert.Contains("/new/", result);
        Assert.DoesNotContain("/old/", result);
    }

    // Regression: when the LiveTicker page unmounted, its Chart.js head contribution
    // dropped out of the registry. The client morph walked head children positionally,
    // hit a tag-name mismatch at the shifted slot, and REPLACED the scoped-css <link>
    // — which dropped its stylesheet rules and produced a visible flicker. Emitting a
    // stable data-rask-key on every head asset switches the morph into its keyed
    // branch, which matches by identity instead of position and moves nodes rather
    // than destroying them.
    [Fact]
    public void ApplyTo_EmitsDataRaskKey_OnEveryUserAsset()
    {
        var registry = new HeadAssetRegistry();
        registry.Add(Link(Rel: "stylesheet", Href: "/bootstrap.css"));
        registry.Add(Meta("utf-8"));
        registry.Add(Title()["Page"]);

        var html = $"<head>{HeadAssetRegistry.Sentinel}</head>";
        var result = registry.ApplyTo(html);

        Assert.Equal(3, CountOccurrences(result, "data-rask-key=\""));
        Assert.Contains("data-rask-key=\"tag:title\"", result);
        Assert.Contains("data-rask-key=\"h-", result);
    }

    [Fact]
    public void ApplyTo_ContentHash_StableForIdenticalHtml()
    {
        // Two separate registries given the same HTML must produce the same
        // data-rask-key — that's what lets the morph match an unchanged asset
        // (e.g. App's Bootstrap link) across renders.
        var a = new HeadAssetRegistry();
        a.Add(Link(Rel: "stylesheet", Href: "/bootstrap.css"));
        var b = new HeadAssetRegistry();
        b.Add(Link(Rel: "stylesheet", Href: "/bootstrap.css"));

        var aHtml = a.ApplyTo($"<head>{HeadAssetRegistry.Sentinel}</head>");
        var bHtml = b.ApplyTo($"<head>{HeadAssetRegistry.Sentinel}</head>");

        var aKey = ExtractKey(aHtml);
        var bKey = ExtractKey(bHtml);
        Assert.NotNull(aKey);
        Assert.Equal(aKey, bKey);
    }

    // The legacy IRaskScopedStyles/Scripts strategy is gone; per-component asset emission
    // (one <link>/<script> per mounted type with a registered asset) is covered end-to-end
    // by HeadAssetEmissionTests in this same test project, which exercises the new
    // HeadAssetRegistry.EmitMountedAssets pathway directly.

    [Fact]
    public void ApplyTo_PreservesUserSuppliedDataRaskKey()
    {
        // If a user explicitly placed a data-rask-key on a head asset, the framework
        // must not inject a second one — the user is opting into bespoke morph
        // identity (e.g. for a hand-managed link they want morphed in-place).
        var registry = new HeadAssetRegistry();
        registry.Add(Link(
            Rel: "stylesheet",
            Href: "/x.css",
            Data: new Dictionary<string, string?> { ["rask-key"] = "user-link" }));

        var html = $"<head>{HeadAssetRegistry.Sentinel}</head>";
        var result = registry.ApplyTo(html);

        Assert.Equal(1, CountOccurrences(result, "data-rask-key=\""));
        Assert.Contains("data-rask-key=\"user-link\"", result);
    }

    private static string? ExtractKey(string html)
    {
        const string needle = "data-rask-key=\"";
        var start = html.IndexOf(needle, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += needle.Length;
        var end = html.IndexOf('"', start);
        return end < 0 ? null : html.Substring(start, end - start);
    }

    [Fact]
    public void Add_FragmentChildrenFlatten()
    {
        var registry = new HeadAssetRegistry();
        registry.Add([
            Link(Rel: "stylesheet", Href: "/x.css"),
            Script("/x.js")
        ]);

        var html = $"<head>{HeadAssetRegistry.Sentinel}</head>";
        var result = registry.ApplyTo(html);

        Assert.Contains("/x.css", result);
        Assert.Contains("/x.js", result);
    }

    [Fact]
    public void ApplyTo_NoSentinel_ReturnsUnchanged()
    {
        var registry = new HeadAssetRegistry();
        registry.Add(Link(Rel: "stylesheet", Href: "/a.css"));
        var input = "<head><title>x</title></head>";
        Assert.Equal(input, registry.ApplyTo(input));
    }

    [Fact]
    public void ApplyTo_NoEntries_StripsSentinel()
    {
        var registry = new HeadAssetRegistry();
        var input = $"<head>{HeadAssetRegistry.Sentinel}</head>";
        Assert.Equal("<head></head>", registry.ApplyTo(input));
    }

    // Regression for the in-place splice: RenderAsLiveRoot records the FIRST head's sentinel
    // offset and ApplyInPlace must replace exactly that one occurrence, leaving any later
    // sentinel (a stray nested <head> that slipped the RASK019 analyzer) as a literal — matching
    // the old html.IndexOf(Sentinel) first-occurrence behaviour rather than mis-splicing a
    // duplicate. Both the live-root path and ApplyTo funnel through ApplyInPlace, so this locks
    // the shared splice body's "one sentinel at the given index" contract.
    [Fact]
    public void ApplyInPlace_SplicesOnlyTheSentinelAtTheGivenIndex()
    {
        var registry = new HeadAssetRegistry();
        registry.Add(Link(Rel: "stylesheet", Href: "/a.css"));

        var raw = $"<head>{HeadAssetRegistry.Sentinel}</head>"
                  + $"<body><head>{HeadAssetRegistry.Sentinel}</head></body>";
        var firstIdx = raw.IndexOf(HeadAssetRegistry.Sentinel, StringComparison.Ordinal);
        var page = new StringBuilder(raw);

        registry.ApplyInPlace(page, firstIdx);
        var result = page.ToString();

        // The first sentinel became the asset; exactly one (the later, un-targeted) sentinel remains.
        Assert.Equal(1, CountOccurrences(result, HeadAssetRegistry.Sentinel));
        Assert.Contains("/a.css", result);
        Assert.True(
            result.IndexOf("/a.css", StringComparison.Ordinal)
            < result.IndexOf(HeadAssetRegistry.Sentinel, StringComparison.Ordinal),
            "asset should splice at the first head; the surviving sentinel is the later one");
    }

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
}
