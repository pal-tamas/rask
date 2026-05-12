using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

public class RouteRegistryTests : IDisposable
{
    public RouteRegistryTests() => RouteRegistry.Reset();
    public void Dispose() => RouteRegistry.Reset();

    [Fact]
    public void BuildTree_NoRegistrations_ReturnsEmpty() => Assert.Empty(RouteRegistry.BuildTree());

    [Fact]
    public void BuildTree_FlatRegistrations_AllRoots()
    {
        RouteRegistry.Add(new[]
        {
            new RouteRegistration(typeof(A), "/a", null), new RouteRegistration(typeof(B), "/b", null)
        });

        var tree = RouteRegistry.BuildTree();

        Assert.Equal(2, tree.Count);
        Assert.Contains(tree, r => r.PageType == typeof(A) && r.Template == "/a");
        Assert.Contains(tree, r => r.PageType == typeof(B) && r.Template == "/b");
        Assert.All(tree, r => Assert.True(r.SubRoutes is null || r.SubRoutes.Count == 0));
    }

    [Fact]
    public void BuildTree_NestedRegistrations_BuildsChildrenUnderParent()
    {
        RouteRegistry.Add(new[]
        {
            new RouteRegistration(typeof(A), "/a", null), new RouteRegistration(typeof(B), "b", typeof(A)),
            new RouteRegistration(typeof(C), "c", typeof(A))
        });

        var tree = RouteRegistry.BuildTree();

        var root = Assert.Single(tree);
        Assert.Equal(typeof(A), root.PageType);
        Assert.NotNull(root.SubRoutes);
        Assert.Equal(2, root.SubRoutes!.Count);
        Assert.Contains(root.SubRoutes, r => r.PageType == typeof(B));
        Assert.Contains(root.SubRoutes, r => r.PageType == typeof(C));
    }

    [Fact]
    public void BuildTree_OrphanChild_DroppedFromTree()
    {
        RouteRegistry.Add(new[]
        {
            new RouteRegistration(typeof(A), "/a", null),
            new RouteRegistration(typeof(B), "b", typeof(C)) // C never registered
        });

        var tree = RouteRegistry.BuildTree();

        var root = Assert.Single(tree);
        Assert.Equal(typeof(A), root.PageType);
        Assert.DoesNotContain(tree, r => r.PageType == typeof(B));
    }

    [Fact]
    public void BuildTree_IsCached_AcrossCalls()
    {
        RouteRegistry.Add(new[] { new RouteRegistration(typeof(A), "/a", null) });

        var first = RouteRegistry.BuildTree();
        var second = RouteRegistry.BuildTree();

        Assert.Same(first, second);
    }

    [Fact]
    public void Add_AfterBuildTree_InvalidatesCache()
    {
        RouteRegistry.Add(new[] { new RouteRegistration(typeof(A), "/a", null) });
        var first = RouteRegistry.BuildTree();

        RouteRegistry.Add(new[] { new RouteRegistration(typeof(B), "/b", null) });
        var second = RouteRegistry.BuildTree();

        Assert.NotSame(first, second);
        Assert.Equal(2, second.Count);
    }

    private sealed class A : Component
    {
        protected override Component Render() => this;
    }

    private sealed class B : Component
    {
        protected override Component Render() => this;
    }

    private sealed class C : Component
    {
        protected override Component Render() => this;
    }
}
