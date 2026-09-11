using Rask.Server.Prerender;

namespace Rask.Server.Tests.Prerender;

// Which requests may be answered from a stored copy. Every "no" here is a request served live exactly as it
// was before the cache existed, so the table errs towards live whenever a copy could be wrong.
public class PageCacheEligibilityTests
{
    [Fact]
    public void AnOrdinaryRequestForAPlannedPublicPage_MayBeAnswered()
    {
        Assert.Equal(PageCacheBypass.None, Evaluate());
    }

    [Fact]
    public void AnythingButGetOrHead_IsLive()
    {
        Assert.Equal(PageCacheBypass.Method, Evaluate(getOrHead: false));
    }

    [Fact]
    public void AQueryString_IsLive()
    {
        // A page can read the query and nothing can tell whether it did; keying on it would let anyone mint
        // copies without limit.
        Assert.Equal(PageCacheBypass.Query, Evaluate(hasQuery: true));
    }

    [Fact]
    public void AMountedApplication_IsLive()
    {
        // The operator console at /_rask is not the host's page to share.
        Assert.Equal(PageCacheBypass.Mounted, Evaluate(mounted: true));
    }

    [Fact]
    public void TheNotFoundPage_IsLive()
    {
        Assert.Equal(PageCacheBypass.NotFound, Evaluate(resolvedToPage: false));
    }

    [Fact]
    public void APathTheAppDidNotPlan_IsLive()
    {
        // /products/{id} for an id no IPrerenderPaths named: the bound that keeps traffic from growing the cache.
        Assert.Equal(PageCacheBypass.Unplanned, Evaluate(planned: false));
    }

    [Fact]
    public void ALanguageChosenInTheUrl_IsLive()
    {
        // That request remembers the choice in a cookie, which only a live response sets.
        Assert.Equal(PageCacheBypass.CultureFromQuery, Evaluate(cultureFromQuery: true));
    }

    [Fact]
    public void AGuardedPage_IsLive()
    {
        Assert.Equal(PageCacheBypass.Protected, Evaluate(isPublic: false));
    }

    [Fact]
    public void TheFirstReasonThatAppliesIsTheOneReported()
    {
        // Cheapest first, and each tag names what a reader would change to get the page cached.
        Assert.Equal(PageCacheBypass.Method, Evaluate(getOrHead: false, hasQuery: true, isPublic: false));
        Assert.Equal(PageCacheBypass.Query, Evaluate(hasQuery: true, planned: false));
    }

    [Theory]
    // By name: the enum is internal, and a public test method cannot take it as a parameter.
    [InlineData(nameof(PageCacheBypass.None), "none")]
    [InlineData(nameof(PageCacheBypass.Unplanned), "unplanned")]
    [InlineData(nameof(PageCacheBypass.SignedInReadsUser), "readsuser")]
    [InlineData(nameof(PageCacheBypass.Refused), "refused")]
    public void EachReasonHasAStableMetricTag(string bypass, string tag)
    {
        Assert.Equal(tag, PageCacheEligibility.Tag(Enum.Parse<PageCacheBypass>(bypass)));
    }

    [Fact]
    public void EveryReasonHasATagOfItsOwn()
    {
        // A dashboard groups by the tag; two reasons sharing one would read as a single cause.
        var tags = Enum.GetValues<PageCacheBypass>().Select(PageCacheEligibility.Tag).ToList();

        Assert.DoesNotContain("unknown", tags);
        Assert.Equal(tags.Count, tags.Distinct(StringComparer.Ordinal).Count());
    }

    private static PageCacheBypass Evaluate(
        bool getOrHead = true,
        bool hasQuery = false,
        bool mounted = false,
        bool resolvedToPage = true,
        bool planned = true,
        bool cultureFromQuery = false,
        bool isPublic = true) =>
        PageCacheEligibility.Evaluate(getOrHead, hasQuery, mounted, resolvedToPage, planned, cultureFromQuery, isPublic);
}
