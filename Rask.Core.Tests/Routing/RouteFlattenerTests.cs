using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

public class RouteFlattenerTests
{
    [Fact]
    public void Flatten_SingleLevel_ProducesOneLeafPerRoute()
    {
        var roots = new[] { new Route(typeof(HomePage), "/"), new Route(typeof(UserPage), "/users/{id}") };

        var leaves = RouteFlattener.Flatten(roots);

        Assert.Equal(2, leaves.Count);
        Assert.Contains(leaves, l => l.FullTemplate == "/" && l.Chain.Count == 1 && l.Chain[0] == typeof(HomePage));
        Assert.Contains(leaves, l => l.FullTemplate == "/users/{id}" && l.Chain[0] == typeof(UserPage));
    }

    [Fact]
    public void Flatten_NestedRoutes_JoinsTemplatesAndBuildsChain()
    {
        var roots = new[]
        {
            new Route(typeof(DashPage), "/dashboard",
                new[]
                {
                    new Route(typeof(DashHome), ""), new Route(typeof(DashOverview), "overview"),
                    new Route(typeof(DashSettings), "settings/{tab}")
                })
        };

        var leaves = RouteFlattener.Flatten(roots);

        Assert.Equal(3, leaves.Count);
        Assert.Contains(leaves,
            l => l.FullTemplate == "/dashboard" && l.Chain.SequenceEqual(new[] { typeof(DashPage), typeof(DashHome) }));
        Assert.Contains(leaves,
            l => l.FullTemplate == "/dashboard/overview" &&
                 l.Chain.SequenceEqual(new[] { typeof(DashPage), typeof(DashOverview) }));
        Assert.Contains(leaves,
            l => l.FullTemplate == "/dashboard/settings/{tab}" &&
                 l.Chain.SequenceEqual(new[] { typeof(DashPage), typeof(DashSettings) }));
    }

    [Fact]
    public void Flatten_ThreeDeep_ProducesFullChain()
    {
        var roots = new[]
        {
            new Route(typeof(DashPage), "/a",
                new[] { new Route(typeof(DashOverview), "b", new[] { new Route(typeof(DeepLeaf), "c") }) })
        };

        var leaves = RouteFlattener.Flatten(roots);

        var only = Assert.Single(leaves);
        Assert.Equal("/a/b/c", only.FullTemplate);
        Assert.Equal(new[] { typeof(DashPage), typeof(DashOverview), typeof(DeepLeaf) }, only.Chain);
    }

    [Fact]
    public void Flatten_LiteralBeforeParam_RegardlessOfDeclarationOrder()
    {
        var roots = new[] { new Route(typeof(UserPage), "/users/{id}"), new Route(typeof(UserNew), "/users/new") };

        var leaves = RouteFlattener.Flatten(roots);

        Assert.Equal("/users/new", leaves[0].FullTemplate);
        Assert.Equal("/users/{id}", leaves[1].FullTemplate);
    }

    [Theory]
    [InlineData("", "", "/")]
    [InlineData("/", "", "/")]
    [InlineData("", "/", "/")]
    [InlineData("/dashboard", "", "/dashboard")]
    [InlineData("/dashboard", "overview", "/dashboard/overview")]
    [InlineData("/dashboard", "/overview", "/dashboard/overview")]
    [InlineData("/dashboard/", "overview", "/dashboard/overview")]
    [InlineData("/users/{id}", "posts/{postId}", "/users/{id}/posts/{postId}")]
    public void Combine_NormalisesSlashes(string parent, string child, string expected) =>
        Assert.Equal(expected, RouteFlattener.Combine(parent, child));

    private sealed class HomePage : Component
    {
        protected override Component Render() => Span();
    }

    private sealed class UserPage : Component
    {
        protected override Component Render() => Span();
    }

    private sealed class UserNew : Component
    {
        protected override Component Render() => Span();
    }

    private sealed class DashPage : Component
    {
        protected override Component Render() => Span();
    }

    private sealed class DashHome : Component
    {
        protected override Component Render() => Span();
    }

    private sealed class DashOverview : Component
    {
        protected override Component Render() => Span();
    }

    private sealed class DashSettings : Component
    {
        protected override Component Render() => Span();
    }

    private sealed class DeepLeaf : Component
    {
        protected override Component Render() => Span();
    }
}
