using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Routing;
using Rask.Server.Prerender;
using Rask.Server.Tests.Endpoints;

namespace Rask.Server.Tests.Prerender;

// What happens to a page's stored copy after a background render, and how the budget and the back-off
// hold up — the parts of the cache no request can see directly.
public class PageCacheTests
{
    private const string Small = "<p>a</p>";
    private static readonly PageCacheKey About = new("/about", string.Empty, string.Empty);
    private static readonly PageStoreVerdict StoreIt = new(PageStoreDecision.Store, string.Empty);

    [Fact]
    public void AStoredCopy_IsCountedAgainstTheBudget()
    {
        var cache = Cache();

        Assert.Equal("stored", Apply(cache, StoreIt, Small));

        Assert.Equal(1, cache.Count);
        Assert.Equal(Small.Length, cache.Bytes);
    }

    [Fact]
    public void TheSameBytesAgain_AreUnchanged_AndCostNothingMore()
    {
        var cache = Cache();
        Apply(cache, StoreIt, Small);

        Assert.Equal("unchanged", Apply(cache, StoreIt, Small));
        Assert.Equal(Small.Length, cache.Bytes);
    }

    [Fact]
    public void AnEvictedPage_IsServedLiveForAWhile()
    {
        var cache = Cache();
        Apply(cache, StoreIt, Small);

        Assert.Equal("evicted", Apply(cache, new PageStoreVerdict(PageStoreDecision.Evict, "it answered 404"), null));

        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.Bytes);
        Assert.True(cache.IsRefused(About));
    }

    [Fact]
    public void AFailedRefresh_KeepsTheCopyItHad()
    {
        var cache = Cache();
        Apply(cache, StoreIt, Small);

        var outcome = Apply(cache, new PageStoreVerdict(PageStoreDecision.KeepStale, "its render threw"), null);

        Assert.Equal("kept", outcome);
        Assert.Equal(1, cache.Count);
        Assert.False(cache.IsRefused(About));
    }

    [Fact]
    public void APageThatOutgrowsTheBudget_KeepsServingTheCopyItHad()
    {
        // Refusing it would send every request live for minutes while its old bytes sat in the budget unused.
        var cache = Cache(new RaskServerLimits { Prerender = true, PageCacheMaxBytes = 20 });
        Apply(cache, StoreIt, Small);

        var outcome = Apply(cache, StoreIt, "<p>" + new string('b', 40) + "</p>");

        Assert.Equal("kept", outcome);
        Assert.Equal(1, cache.Count);
        Assert.Equal(Small.Length, cache.Bytes);
        Assert.False(cache.IsRefused(About));
    }

    [Fact]
    public void APageWithNoCopyThatDoesNotFit_IsServedLive()
    {
        var cache = Cache(new RaskServerLimits { Prerender = true, PageCacheMaxBytes = 4 });

        Assert.Equal("refused", Apply(cache, StoreIt, Small));

        Assert.Equal(0, cache.Count);
        Assert.True(cache.IsRefused(About));
    }

    [Fact]
    public void APageThePlanDroppedWhileItRendered_IsNotStoredAgain()
    {
        var cache = Cache();
        Apply(cache, StoreIt, Small);

        cache.Planned.Refresh([], NoSources(), maxPaths: 100);
        cache.PruneUnplanned();

        Assert.Equal("evicted", Apply(cache, StoreIt, Small));
        Assert.Equal(0, cache.Count);
        Assert.Equal(0, cache.Bytes);
    }

    [Fact]
    public void AnEnormousFreshnessBound_StillBacksOff()
    {
        // TimeSpan.MaxValue once overflowed the back-off to a negative timestamp, which reads as "now" — so a
        // refused page was queued for a render on every request.
        var cache = Cache(new RaskServerLimits { Prerender = true, RevalidateAfter = TimeSpan.MaxValue });

        Apply(cache, new PageStoreVerdict(PageStoreDecision.Evict, "it redirects"), null);

        Assert.True(cache.IsRefused(About));
    }

    private static string Apply(PageCache cache, PageStoreVerdict verdict, string? document) =>
        cache.Apply(About, verdict, document, readsUser: false, cssBundle: "css", jsBundle: "js");

    private static PageCache Cache(RaskServerLimits? limits = null)
    {
        var cache = new PageCache(limits ?? new RaskServerLimits { Prerender = true }, TimeProvider.System);
        cache.Planned.Refresh([new Route(typeof(ContentOnlyApp), "/about")], NoSources(), maxPaths: 100);
        return cache;
    }

    private static IServiceScopeFactory NoSources() =>
        new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
}
