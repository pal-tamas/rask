using System.Reflection;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Example.Shared.Pages;
using Rask.Example.Shared.Tests.Infrastructure;

namespace Rask.Example.Shared.Tests.Pages;

public sealed class LifecyclePageTests
{
    [Fact]
    public void Render_AtRest_ShowsProbeNotMounted_AndEmptyLog()
    {
        var routeState = new RouteState { Path = "/lifecycle" };
        var html = new Rask.Example.Shared.App()
            .RenderAsLiveRoot(TestServices.Default(routeState: routeState));

        Assert.Contains("Probe not mounted.", html);
        Assert.Contains("Empty", html);
        Assert.Contains("lifecycle-cycle-mount", html);
        Assert.Contains("lifecycle-cycle-unmount", html);
    }

    [Fact]
    public void MountCycle_AddsProbe_BumpsId_FlipsMountedFlag()
    {
        var page = new LifecyclePage();
        Invoke(page, "MountCycle");
        Assert.Equal(1, GetField<int>(page, "_nextCycleId"));
        Assert.True(GetField<bool>(page, "_cycleMounted"));
    }

    [Fact]
    public void UnmountCycle_FromMounted_FlipsFlagBack()
    {
        var page = new LifecyclePage();
        Invoke(page, "MountCycle");
        Invoke(page, "UnmountCycle");
        Assert.False(GetField<bool>(page, "_cycleMounted"));
    }

    [Fact]
    public void MountCycle_WhenAlreadyMounted_DoesNotIncrementId()
    {
        var page = new LifecyclePage();
        Invoke(page, "MountCycle");
        Invoke(page, "MountCycle");
        Assert.Equal(1, GetField<int>(page, "_nextCycleId"));
    }

    [Fact]
    public void AppendCycleLog_AppendsLineToCycleLog()
    {
        var page = new LifecyclePage();
        var mi = typeof(LifecyclePage).GetMethod("AppendCycleLog",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        mi.Invoke(page, ["hello"]);
        var log = GetField<List<string>>(page, "_cycleLog");
        Assert.Contains("hello", log);
    }

    private static void Invoke(LifecyclePage page, string method)
    {
        var mi = typeof(LifecyclePage).GetMethod(method,
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        mi.Invoke(page, null);
    }

    private static T GetField<T>(LifecyclePage page, string name)
    {
        var f = typeof(LifecyclePage).GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (T)f.GetValue(page)!;
    }
}
