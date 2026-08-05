using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

/// <summary>
///     Covers <see cref="RouteRegistry.Replace" />, the keyed-group entry point that makes routes
///     hot-reloadable. Before it existed the generated <c>__RaskRoutesRegistry</c> only had an
///     additive <see cref="RouteRegistry.Add" /> behind a <c>[ModuleInitializer]</c>, so editing a
///     <c>[Route]</c> template under <c>dotnet watch</c> did nothing at all — and re-running the
///     initializer to fix that would have duplicated every route instead.
/// </summary>
[Collection("RouteRegistry")]
public class RouteRegistryHotReloadTests : IDisposable
{
    private static readonly object _asmOne = new();
    private static readonly object _asmTwo = new();

    public RouteRegistryHotReloadTests() => RouteRegistry.Reset();
    public void Dispose() => RouteRegistry.Reset();

    [Fact]
    public void Replace_SameKeyTwice_ReplacesRatherThanAppends()
    {
        RouteRegistry.Replace(_asmOne, new[] { new RouteRegistration(typeof(A), "/a", null) });
        RouteRegistry.Replace(_asmOne, new[] { new RouteRegistration(typeof(A), "/a2", null) });

        // The whole point: Add() would have left two nodes for A here.
        var route = Assert.Single(RouteRegistry.BuildTree());
        Assert.Equal(typeof(A), route.PageType);
        Assert.Equal("/a2", route.Template);
    }

    [Fact]
    public void Replace_DropsARouteDeletedFromTheGroup()
    {
        RouteRegistry.Replace(_asmOne, new[]
        {
            new RouteRegistration(typeof(A), "/a", null), new RouteRegistration(typeof(B), "/b", null)
        });

        RouteRegistry.Replace(_asmOne, new[] { new RouteRegistration(typeof(A), "/a", null) });

        var route = Assert.Single(RouteRegistry.BuildTree());
        Assert.Equal(typeof(A), route.PageType);
    }

    [Fact]
    public void Replace_PreservesOtherContributingAssembliesGroups()
    {
        // Two assemblies each contribute routes; refreshing one must not disturb the other. A
        // clear-then-reinvoke-everything design would drop whichever assembly refreshed first.
        RouteRegistry.Replace(_asmOne, new[] { new RouteRegistration(typeof(A), "/a", null) });
        RouteRegistry.Replace(_asmTwo, new[] { new RouteRegistration(typeof(B), "/b", null) });

        RouteRegistry.Replace(_asmOne, new[] { new RouteRegistration(typeof(A), "/a2", null) });

        var tree = RouteRegistry.BuildTree();
        Assert.Equal(2, tree.Count);
        Assert.Contains(tree, r => r.PageType == typeof(A) && r.Template == "/a2");
        Assert.Contains(tree, r => r.PageType == typeof(B) && r.Template == "/b");
    }

    [Fact]
    public void Replace_PreservesManualAddRegistrations()
    {
        RouteRegistry.Add(new[] { new RouteRegistration(typeof(C), "/c", null) });
        RouteRegistry.Replace(_asmOne, new[] { new RouteRegistration(typeof(A), "/a", null) });

        RouteRegistry.Replace(_asmOne, new[] { new RouteRegistration(typeof(A), "/a2", null) });

        var tree = RouteRegistry.BuildTree();
        Assert.Equal(2, tree.Count);
        Assert.Contains(tree, r => r.PageType == typeof(C) && r.Template == "/c");
    }

    [Fact]
    public void Replace_DoesNotClearTheDefaultFallback()
    {
        // The sharp edge. _defaultFallback is seeded once by __RaskDefaultFallback's
        // [ModuleInitializer], which never re-runs — so a refresh implemented via Reset() would
        // make every 404 after the first hot reload an unhandled route, invisibly.
        RouteRegistry.SetDefaultFallback(typeof(Fallback));
        RouteRegistry.Replace(_asmOne, new[] { new RouteRegistration(typeof(A), "/a", null) });

        RouteRegistry.Replace(_asmOne, new[] { new RouteRegistration(typeof(A), "/a2", null) });

        var tree = RouteRegistry.BuildTree();
        Assert.Contains(tree, r => r.PageType == typeof(Fallback)
                                   && r.Template == RouteRegistry.DefaultFallbackTemplate);
    }

    [Fact]
    public void Replace_InvalidatesTheTreeCache()
    {
        RouteRegistry.Replace(_asmOne, new[] { new RouteRegistration(typeof(A), "/a", null) });
        var before = RouteRegistry.BuildTree();

        RouteRegistry.Replace(_asmOne, new[] { new RouteRegistration(typeof(A), "/a2", null) });
        var after = RouteRegistry.BuildTree();

        Assert.NotSame(before, after);
        Assert.Equal("/a2", Assert.Single(after).Template);
    }

    [Fact]
    public void Replace_WithIdenticalRegistrations_KeepsTheCachedTree()
    {
        // Every RefreshAll() re-runs on every apply, including reloads that touched no route at
        // all. Re-registering identical content must not churn the tree.
        var items = new[] { new RouteRegistration(typeof(A), "/a", null) };
        RouteRegistry.Replace(_asmOne, items);
        var before = RouteRegistry.BuildTree();

        RouteRegistry.Replace(_asmOne, new[] { new RouteRegistration(typeof(A), "/a", null) });

        Assert.Same(before, RouteRegistry.BuildTree());
    }

    [Fact]
    public void Replace_PreservesParentChildNesting()
    {
        RouteRegistry.Replace(_asmOne, new[]
        {
            new RouteRegistration(typeof(A), "/a", null), new RouteRegistration(typeof(B), "b", typeof(A))
        });

        RouteRegistry.Replace(_asmOne, new[]
        {
            new RouteRegistration(typeof(A), "/a", null), new RouteRegistration(typeof(B), "b2", typeof(A))
        });

        var root = Assert.Single(RouteRegistry.BuildTree());
        Assert.Equal(typeof(A), root.PageType);
        var child = Assert.Single(root.SubRoutes!);
        Assert.Equal(typeof(B), child.PageType);
        Assert.Equal("b2", child.Template);
    }

    [Fact]
    public void Add_StillAppends()
    {
        // Regression guard: Replace must not have changed Add's public contract.
        RouteRegistry.Add(new[] { new RouteRegistration(typeof(A), "/a", null) });
        RouteRegistry.Add(new[] { new RouteRegistration(typeof(B), "/b", null) });

        Assert.Equal(2, RouteRegistry.BuildTree().Count);
    }

    [Fact]
    public void Replace_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() =>
            RouteRegistry.Replace(null!, Array.Empty<RouteRegistration>()));
        Assert.Throws<ArgumentNullException>(() => RouteRegistry.Replace(_asmOne, null!));
    }

    private sealed class A;

    private sealed class B;

    private sealed class C;

    private sealed class Fallback;
}
