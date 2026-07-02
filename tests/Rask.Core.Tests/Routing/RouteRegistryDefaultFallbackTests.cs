using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

[Collection("RouteRegistry")]
public class RouteRegistryDefaultFallbackTests : IDisposable
{
    public RouteRegistryDefaultFallbackTests() => RouteRegistry.Reset();
    public void Dispose() => RouteRegistry.Reset();

    [Fact]
    public void BuildTree_OnlyFallbackSet_IncludesSyntheticCatchAll()
    {
        RouteRegistry.SetDefaultFallback(typeof(Fallback));

        var tree = RouteRegistry.BuildTree();

        var root = Assert.Single(tree);
        Assert.Equal(typeof(Fallback), root.PageType);
        Assert.Equal("{**__rask_notfound}", root.Template);
    }

    [Fact]
    public void BuildTree_FallbackPlusTypedRoute_BothPresent()
    {
        RouteRegistry.SetDefaultFallback(typeof(Fallback));
        RouteRegistry.Add(new[] { new RouteRegistration(typeof(Home), "/", null) });

        var tree = RouteRegistry.BuildTree();

        Assert.Equal(2, tree.Count);
        Assert.Contains(tree, r => r.PageType == typeof(Home));
        Assert.Contains(tree, r => r.PageType == typeof(Fallback));
    }

    [Fact]
    public void BuildTree_UserCatchAllRegistered_FallbackOmitted()
    {
        RouteRegistry.SetDefaultFallback(typeof(Fallback));
        RouteRegistry.Add(new[] { new RouteRegistration(typeof(UserNotFound), "{**rest}", null) });

        var tree = RouteRegistry.BuildTree();

        Assert.DoesNotContain(tree, r => r.PageType == typeof(Fallback));
        Assert.Contains(tree, r => r.PageType == typeof(UserNotFound));
    }

    [Fact]
    public void Reset_ClearsFallback()
    {
        RouteRegistry.SetDefaultFallback(typeof(Fallback));
        RouteRegistry.Reset();

        Assert.Empty(RouteRegistry.BuildTree());
    }

    [Fact]
    public void SetDefaultFallback_AfterBuildTree_InvalidatesCache()
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
