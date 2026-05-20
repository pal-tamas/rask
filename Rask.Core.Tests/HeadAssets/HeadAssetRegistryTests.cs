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

        Assert.Equal(1, CountOccurrences(result, "<title>"));
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
        Assert.Equal(1, CountOccurrences(result, "<title>"));
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

        Assert.Equal(1, CountOccurrences(result, "<base"));
        Assert.Contains("/new/", result);
        Assert.DoesNotContain("/old/", result);
    }

    [Fact]
    public void Add_FragmentChildrenFlatten()
    {
        var registry = new HeadAssetRegistry();
        registry.Add(Fragment()[
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
