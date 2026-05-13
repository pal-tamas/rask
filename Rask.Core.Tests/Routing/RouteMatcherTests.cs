using Microsoft.AspNetCore.Http;
using Rask.Core.Components;
using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

public class RouteMatcherTests
{
    private static IReadOnlyList<RouteLeaf> Flat(params Route[] routes) => RouteFlattener.Flatten(routes);

    [Fact]
    public void TryMatch_LiteralRoot_Matches()
    {
        var leaves = Flat(new Route(typeof(HomePage), "/"));

        Assert.True(RouteMatcher.TryMatch(leaves, new PathString("/"), out var chain, out var values));
        Assert.Equal(typeof(HomePage), chain[0]);
        Assert.Empty(values);
    }

    [Fact]
    public void TryMatch_SingleParam_BindsValue()
    {
        var leaves = Flat(new Route(typeof(UserPage), "/users/{id}"));

        Assert.True(RouteMatcher.TryMatch(leaves, new PathString("/users/42"), out var chain, out var values));
        Assert.Equal(typeof(UserPage), chain[0]);
        Assert.Equal("42", values["id"]);
    }

    [Fact]
    public void TryMatch_MultipleParams_BindsAll()
    {
        var leaves = Flat(new Route(typeof(OrgUserPage), "/orgs/{org}/users/{id}"));

        Assert.True(RouteMatcher.TryMatch(leaves, new PathString("/orgs/acme/users/42"), out _, out var values));
        Assert.Equal("acme", values["org"]);
        Assert.Equal("42", values["id"]);
    }

    [Fact]
    public void TryMatch_NoMatch_ReturnsFalse()
    {
        var leaves = Flat(new Route(typeof(HomePage), "/"));

        Assert.False(RouteMatcher.TryMatch(leaves, new PathString("/missing"), out _, out _));
    }

    [Fact]
    public void TryMatch_LiteralWinsOverParam()
    {
        var leaves = Flat(
            new Route(typeof(UserPage), "/users/{id}"),
            new Route(typeof(UserNew), "/users/new"));

        Assert.True(RouteMatcher.TryMatch(leaves, new PathString("/users/new"), out var chain, out _));
        Assert.Equal(typeof(UserNew), chain[0]);
    }

    [Fact]
    public void TryMatch_NestedSubroute_ReturnsFullChainAndMergedValues()
    {
        var leaves = Flat(new Route(typeof(DashPage), "/dashboard",
            new[] { new Route(typeof(DashOverview), "overview/{tab}") }));

        Assert.True(RouteMatcher.TryMatch(leaves, new PathString("/dashboard/overview/billing"), out var chain,
            out var values));
        Assert.Equal(new[] { typeof(DashPage), typeof(DashOverview) }, chain);
        Assert.Equal("billing", values["tab"]);
    }

    [Fact]
    public void TryMatch_IndexSubroute_MatchesBareParent()
    {
        var leaves = Flat(new Route(typeof(DashPage), "/dashboard",
            new[] { new Route(typeof(DashHome), ""), new Route(typeof(DashOverview), "overview") }));

        Assert.True(RouteMatcher.TryMatch(leaves, new PathString("/dashboard"), out var chain, out _));
        Assert.Equal(new[] { typeof(DashPage), typeof(DashHome) }, chain);
    }

    private sealed class HomePage : Component
    {
        protected override Component Render() => new Span();
    }

    private sealed class UserPage : Component
    {
        protected override Component Render() => new Span();
    }

    private sealed class UserNew : Component
    {
        protected override Component Render() => new Span();
    }

    private sealed class OrgUserPage : Component
    {
        protected override Component Render() => new Span();
    }

    private sealed class DashPage : Component
    {
        protected override Component Render() => new Span();
    }

    private sealed class DashHome : Component
    {
        protected override Component Render() => new Span();
    }

    private sealed class DashOverview : Component
    {
        protected override Component Render() => new Span();
    }
}
