using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Live;
using Rask.Core.Routing;
using Rask.Server.Prerender;
using Rask.Server.Tests.Endpoints;

namespace Rask.Server.Tests.Prerender;

// The plan is the bound on everything the cache does: only a planned path can have a copy, so what the
// cache holds and renders is a function of what the app declared, never of what someone requests.
public class PlannedPathsTests
{
    private static readonly IReadOnlyList<Route> Routes =
    [
        new Route(typeof(ContentOnlyApp), "/"),
        new Route(typeof(ContentOnlyApp), "/about"),
        new Route(typeof(ContentOnlyApp), "/guides/{slug}"),
        new Route(typeof(ContentOnlyApp), RouteRegistry.DefaultFallbackTemplate),
    ];

    [Fact]
    public void LiteralRoutesArePlanned_AndParameterisedOnesAndTheNotFoundRouteAreNot()
    {
        var paths = PlannedPaths.LiteralPaths(Routes).ToList();

        Assert.Contains("/", paths);
        Assert.Contains("/about", paths);
        Assert.DoesNotContain(paths, p => p.Contains('{', StringComparison.Ordinal));
    }

    [Fact]
    public void AnAppsPrerenderPaths_ExtendThePlan()
    {
        var plan = new PlannedPaths();

        Assert.True(plan.Refresh(Routes, Scopes(new FixedPaths("/guides/a", "/guides/b")), maxPaths: 100));

        Assert.True(plan.Contains("/about"));
        Assert.True(plan.Contains("/guides/a"));
        Assert.True(plan.Contains("/guides/b"));
        Assert.False(plan.Contains("/guides/c"));
    }

    [Theory]
    [InlineData("guides/a")] // not rooted
    [InlineData("/guides/a?tab=2")]
    [InlineData("/guides/a#top")]
    [InlineData("/guides/../admin")]
    [InlineData("/guides/\\admin")]
    [InlineData("")]
    public void ASuppliedPathThatIsNotARoutePath_IsLeftOut(string path)
    {
        var plan = new PlannedPaths();

        plan.Refresh(Routes, Scopes(new FixedPaths(path)), maxPaths: 100);

        Assert.False(plan.Contains(path));
    }

    [Fact]
    public void ThePlanStopsAtItsCap()
    {
        var plan = new PlannedPaths();

        plan.Refresh(Routes, Scopes(new FixedPaths("/guides/a", "/guides/b", "/guides/c")), maxPaths: 3);

        // "/" and "/about" come first, from the route table; one supplied path fits.
        Assert.Equal(3, plan.Count);
    }

    [Fact]
    public void ASourceThatThrows_KeepsThePlanItHad()
    {
        // Swapping in a plan without the failing source's paths would evict every copy they had, on what is
        // probably a database blip.
        var source = new ToggledPaths("/guides/a");
        var scopes = Scopes(source);
        var plan = new PlannedPaths();
        plan.Refresh(Routes, scopes, maxPaths: 100);

        source.Failure = new InvalidOperationException("the database is away");
        var replaced = plan.Refresh(Routes, scopes, maxPaths: 100);

        Assert.False(replaced);
        Assert.True(plan.Contains("/guides/a"));
    }

    [Fact]
    public void ASourceWhoseCallTimedOut_KeepsThePlanItHad_WithoutThrowing()
    {
        // A synchronous HTTP call inside Paths() that times out throws a cancellation. Treated as the host
        // stopping, it escaped the refresh — and an exception out of a background service stops the host.
        var source = new ToggledPaths("/guides/a");
        var scopes = Scopes(source);
        var plan = new PlannedPaths();
        plan.Refresh(Routes, scopes, maxPaths: 100);

        source.Failure = new TaskCanceledException("the catalogue service did not answer");
        var replaced = plan.Refresh(Routes, scopes, maxPaths: 100);

        Assert.False(replaced);
        Assert.True(plan.Contains("/guides/a"));
    }

    [Fact]
    public void ASourceThatCannotBeResolved_KeepsThePlanItHad_WithoutThrowing()
    {
        // Resolving is where a missing connection string surfaces, before any source is asked anything.
        var plan = new PlannedPaths();
        plan.Refresh(Routes, Scopes(new FixedPaths("/guides/a")), maxPaths: 100);

        var unresolvable = new ServiceCollection()
            .AddScoped<IPrerenderPaths>(_ => throw new InvalidOperationException("no connection string"))
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
        var replaced = plan.Refresh(Routes, unresolvable, maxPaths: 100);

        Assert.False(replaced);
        Assert.True(plan.Contains("/guides/a"));
    }

    private static IServiceScopeFactory Scopes(IPrerenderPaths source) =>
        new ServiceCollection()
            .AddSingleton(source)
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

    private sealed class FixedPaths(params string[] paths) : IPrerenderPaths
    {
        public IEnumerable<string> Paths() => paths;
    }

    private sealed class ToggledPaths(string path) : IPrerenderPaths
    {
        public Exception? Failure { get; set; }

        public IEnumerable<string> Paths() => Failure is { } failure ? throw failure : [path];
    }
}
