using System.Reflection;
using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

// A mounted application's routes are separated from the host's by PROVENANCE — which assembly declared
// them — not by the path they happen to start with. RouteRegistry already grouped registrations per
// assembly for hot reload, and these pin that the grouping is load-bearing for isolation too.
//
// The important assertions here are the NEGATIVE ones. A subset tree that quietly contains the other
// application's routes still resolves every URL correctly; what it gets wrong is which root renders them,
// which no route test would notice.
[Collection("RouteRegistry")]
public class MountedAppRoutesTests : IDisposable
{
    // Two group keys standing in for two assemblies' generated __RaskRoutesRegistry types. Reference
    // identity is what the registry keys on, and Type.Assembly is how provenance is recovered — so a type
    // from THIS assembly and one from another are the two sides needed.
    private static readonly object _hostKey = typeof(MountedAppRoutesTests);
    private static readonly object _mountKey = typeof(string);

    private static Assembly MountAssembly => typeof(string).Assembly;

    public MountedAppRoutesTests()
    {
        RouteRegistry.Reset();
        RouteRegistry.Replace(_hostKey, [new RouteRegistration(typeof(HostPage), "/orders", null)]);
        RouteRegistry.Replace(_mountKey, [new RouteRegistration(typeof(MountPage), "/_rask", null)]);
    }

    public void Dispose()
    {
        RouteRegistry.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void The_mount_sees_its_own_route()
    {
        var tree = RouteRegistry.BuildTree(MountAssembly);
        Assert.Contains(tree, r => r.PageType == typeof(MountPage));
    }

    [Fact]
    public void The_mount_CANNOT_see_the_host_application_s_routes()
    {
        var tree = RouteRegistry.BuildTree(MountAssembly);
        Assert.DoesNotContain(tree, r => r.PageType == typeof(HostPage));
    }

    [Fact]
    public void The_host_CANNOT_see_the_mounted_application_s_routes()
    {
        var tree = RouteRegistry.BuildTreeExcept(MountAssembly);
        Assert.DoesNotContain(tree, r => r.PageType == typeof(MountPage));
    }

    [Fact]
    public void The_host_keeps_its_own_routes()
    {
        var tree = RouteRegistry.BuildTreeExcept(MountAssembly);
        Assert.Contains(tree, r => r.PageType == typeof(HostPage));
    }

    // The trap this design had to avoid: BuildTree appends the default fallback whenever the effective
    // set has no catch-all. Applied to the whole table that is right; applied naively to a subset it
    // would hand the mounted application whatever catch-all the HOST declared, so a mistyped console URL
    // would render the host app's not-found page inside the console's document.
    [Fact]
    public void A_host_catch_all_does_not_answer_inside_the_mounted_application()
    {
        RouteRegistry.Replace(_hostKey,
        [
            new RouteRegistration(typeof(HostPage), "/orders", null),
            new RouteRegistration(typeof(HostNotFound), "{**__rask_notfound}", null),
        ]);

        var mountTree = RouteRegistry.BuildTree(MountAssembly);
        Assert.DoesNotContain(mountTree, r => r.PageType == typeof(HostNotFound));
    }

    [Fact]
    public void The_host_keeps_its_own_catch_all()
    {
        RouteRegistry.Replace(_hostKey,
        [
            new RouteRegistration(typeof(HostPage), "/orders", null),
            new RouteRegistration(typeof(HostNotFound), "{**__rask_notfound}", null),
        ]);

        var hostTree = RouteRegistry.BuildTreeExcept(MountAssembly);
        Assert.Contains(hostTree, r => r.PageType == typeof(HostNotFound));
    }

    // Direct Add() calls name no assembly, so they belong to the host. Giving them to a mount would be
    // the unsafe direction: a mounted app would gain routes nothing shows it declared.
    [Fact]
    public void Manually_added_routes_belong_to_the_host_not_to_a_mount()
    {
        RouteRegistry.Add([new RouteRegistration(typeof(ManualPage), "/manual", null)]);

        Assert.Contains(RouteRegistry.BuildTreeExcept(MountAssembly), r => r.PageType == typeof(ManualPage));
        Assert.DoesNotContain(RouteRegistry.BuildTree(MountAssembly), r => r.PageType == typeof(ManualPage));
    }

    // The subset caches must die with the whole-tree cache, or an edited route keeps being served to one
    // application after the other has already seen the change.
    [Fact]
    public void A_registry_change_invalidates_the_subset_trees_too()
    {
        var before = RouteRegistry.BuildTree(MountAssembly);
        Assert.Contains(before, r => r.PageType == typeof(MountPage));

        RouteRegistry.Replace(_mountKey, [new RouteRegistration(typeof(OtherMountPage), "/_rask", null)]);

        var after = RouteRegistry.BuildTree(MountAssembly);
        Assert.DoesNotContain(after, r => r.PageType == typeof(MountPage));
        Assert.Contains(after, r => r.PageType == typeof(OtherMountPage));
    }

    [Fact]
    public void The_same_subset_is_handed_back_until_the_table_changes() =>
        Assert.Same(RouteRegistry.BuildTree(MountAssembly), RouteRegistry.BuildTree(MountAssembly));

    [Fact]
    public void Excluding_nothing_is_the_whole_table() =>
        Assert.Same(RouteRegistry.BuildTree(), RouteRegistry.BuildTreeExcept([]));

    private sealed partial class HostPage : Component
    {
        protected override Component? Render() => null;
    }

    private sealed partial class HostNotFound : Component
    {
        protected override Component? Render() => null;
    }

    private sealed partial class ManualPage : Component
    {
        protected override Component? Render() => null;
    }

    private sealed partial class MountPage : Component
    {
        protected override Component? Render() => null;
    }

    private sealed partial class OtherMountPage : Component
    {
        protected override Component? Render() => null;
    }
}
