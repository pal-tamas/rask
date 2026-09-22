using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

[Collection("RouteRegistry")]
public class RouteRegistryDefaultFallbackTests : IDisposable
{
    public RouteRegistryDefaultFallbackTests() => RouteRegistry.Reset();
    public void Dispose() => RouteRegistry.Reset();

    [Fact]
    public void With_only_the_fallback_set_the_tree_holds_a_synthetic_catch_all()
    {
        RouteRegistry.SetDefaultFallback(typeof(Fallback));

        var tree = RouteRegistry.BuildTree();

        var root = Assert.Single(tree);
        Assert.Equal(typeof(Fallback), root.PageType);
        Assert.Equal("{**__rask_notfound}", root.Template);
    }

    [Fact]
    public void The_fallback_and_a_typed_route_are_both_in_the_tree()
    {
        RouteRegistry.SetDefaultFallback(typeof(Fallback));
        RouteRegistry.Add(new[] { new RouteRegistration(typeof(Home), "/", null) });

        var tree = RouteRegistry.BuildTree();

        Assert.Equal(2, tree.Count);
        Assert.Contains(tree, r => r.PageType == typeof(Home));
        Assert.Contains(tree, r => r.PageType == typeof(Fallback));
    }

    [Fact]
    public void A_registered_user_catch_all_leaves_the_fallback_out()
    {
        RouteRegistry.SetDefaultFallback(typeof(Fallback));
        RouteRegistry.Add(new[] { new RouteRegistration(typeof(UserNotFound), "{**rest}", null) });

        var tree = RouteRegistry.BuildTree();

        Assert.DoesNotContain(tree, r => r.PageType == typeof(Fallback));
        Assert.Contains(tree, r => r.PageType == typeof(UserNotFound));
    }

    [Fact]
    public void Reset_clears_the_fallback()
    {
        RouteRegistry.SetDefaultFallback(typeof(Fallback));

        RouteRegistry.Reset();

        Assert.Empty(RouteRegistry.BuildTree());
    }

    [Fact]
    public void Setting_the_fallback_after_building_the_tree_invalidates_the_cache()
    {
        RouteRegistry.Add(new[] { new RouteRegistration(typeof(Home), "/", null) });
        var first = RouteRegistry.BuildTree();

        RouteRegistry.SetDefaultFallback(typeof(Fallback));
        var second = RouteRegistry.BuildTree();

        Assert.NotSame(first, second);
        Assert.Equal(2, second.Count);
    }

    private sealed class Fallback : Component
    {
        protected override Component? Render() => this;
    }

    private sealed class Home : Component
    {
        protected override Component? Render() => this;
    }

    private sealed class UserNotFound : Component
    {
        protected override Component? Render() => this;
    }
}
