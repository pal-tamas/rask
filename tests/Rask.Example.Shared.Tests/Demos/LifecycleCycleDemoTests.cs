using System.Reflection;
using Rask.Example.Shared.Features;
using Rask.Example.Shared.Tests.Infrastructure;

namespace Rask.Example.Shared.Tests.Demos;

// LifecycleCycleDemo is the mount/unmount-cycle widget promoted out of the former LifecyclePage when the
// lifecycle pages were folded into the guides. It owns the mount flag, the id counter, and the
// parent-held log that survives the probe's unmount.
public sealed partial class LifecycleCycleDemoTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_AtRest_ShowsProbeNotMounted_AndEmptyLog()
    {
        var host = new LiveHost(() => LifecycleCycleDemo, TestServices.Default());

        var html = host.RenderAsLiveRoot();

        Assert.Contains("Probe not mounted.", html);
        Assert.Contains("Empty", html);
        Assert.Contains("lifecycle-cycle-mount", html);
        Assert.Contains("lifecycle-cycle-unmount", html);
    }

    [Fact]
    public void MountCycle_AddsProbe_BumpsId_FlipsMountedFlag()
    {
        var demo = new LifecycleCycleDemo();
        Invoke(demo, "MountCycle");
        Assert.Equal(1, GetField<int>(demo, "_nextCycleId"));
        Assert.True(GetField<bool>(demo, "_cycleMounted"));
    }

    [Fact]
    public void UnmountCycle_FromMounted_FlipsFlagBack()
    {
        var demo = new LifecycleCycleDemo();
        Invoke(demo, "MountCycle");
        Invoke(demo, "UnmountCycle");
        Assert.False(GetField<bool>(demo, "_cycleMounted"));
    }

    [Fact]
    public void MountCycle_WhenAlreadyMounted_DoesNotIncrementId()
    {
        var demo = new LifecycleCycleDemo();
        Invoke(demo, "MountCycle");
        Invoke(demo, "MountCycle");
        Assert.Equal(1, GetField<int>(demo, "_nextCycleId"));
    }

    [Fact]
    public void AppendCycleLog_AppendsLineToCycleLog()
    {
        var demo = new LifecycleCycleDemo();
        var mi = typeof(LifecycleCycleDemo).GetMethod("AppendCycleLog",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        mi.Invoke(demo, ["hello"]);
        var log = GetField<List<string>>(demo, "_cycleLog");
        Assert.Contains("hello", log);
    }

    private static void Invoke(LifecycleCycleDemo demo, string method)
    {
        var mi = typeof(LifecycleCycleDemo).GetMethod(method,
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        mi.Invoke(demo, null);
    }

    private static T GetField<T>(LifecycleCycleDemo demo, string name)
    {
        var f = typeof(LifecycleCycleDemo).GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (T)f.GetValue(demo)!;
    }
}
