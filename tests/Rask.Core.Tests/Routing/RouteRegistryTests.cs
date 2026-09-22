using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

[Collection("RouteRegistry")]
public class RouteRegistryTests : IDisposable
{
    public RouteRegistryTests() => RouteRegistry.Reset();
    public void Dispose() => RouteRegistry.Reset();

    [Fact]
    public void With_no_registrations_the_tree_is_empty() => Assert.Empty(RouteRegistry.BuildTree());

    [Fact]
    public void Flat_registrations_are_all_roots()
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
    public void Nested_registrations_build_children_under_their_parent()
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
    public void An_orphan_child_is_dropped_from_the_tree()
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
    public void The_tree_is_cached_across_calls()
    {
        RouteRegistry.Add(new[] { new RouteRegistration(typeof(A), "/a", null) });

        var first = RouteRegistry.BuildTree();
        var second = RouteRegistry.BuildTree();

        Assert.Same(first, second);
    }

    [Fact]
    public void Adding_after_building_the_tree_invalidates_the_cache()
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
        protected override Component? Render() => this;
    }

    private sealed class B : Component
    {
        protected override Component? Render() => this;
    }

    private sealed class C : Component
    {
        protected override Component? Render() => this;
    }
}
