using System.Reflection;
using Rask.Site.Features;
using Rask.Site.Tests.Infrastructure;

namespace Rask.Site.Tests.Demos;

// LifecycleCycleDemo is the mount/unmount-cycle widget promoted out of the former LifecyclePage when the
// lifecycle pages were folded into the guides. It owns the mount flag, the id counter, and the
// parent-held log that survives the probe's unmount.
public sealed partial class LifecycleCycleDemoTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void At_rest_the_demo_shows_the_probe_not_mounted_and_an_empty_log()
    {
        var host = new LiveHost(() => LifecycleCycleDemo, TestServices.Default());

        var html = host.RenderAsLiveRoot();

        Assert.Contains("Probe not mounted.", html);
        Assert.Contains("Empty", html);
        Assert.Contains("lifecycle-cycle-mount", html);
        Assert.Contains("lifecycle-cycle-unmount", html);
    }

    [Fact]
    public void Mounting_a_cycle_adds_the_probe_bumps_the_id_and_flips_the_mounted_flag()
    {
        var demo = new LifecycleCycleDemo();

        Invoke(demo, "MountCycle");

        Assert.Equal(1, GetField<int>(demo, "_nextCycleId"));
        Assert.True(GetField<bool>(demo, "_cycleMounted"));
    }

    [Fact]
    public void Unmounting_a_mounted_cycle_flips_the_flag_back()
    {
        var demo = new LifecycleCycleDemo();
        Invoke(demo, "MountCycle");

        Invoke(demo, "UnmountCycle");

        Assert.False(GetField<bool>(demo, "_cycleMounted"));
    }

    [Fact]
    public void Mounting_a_cycle_that_is_already_mounted_does_not_increment_the_id()
    {
        var demo = new LifecycleCycleDemo();
        Invoke(demo, "MountCycle");

        Invoke(demo, "MountCycle");

        Assert.Equal(1, GetField<int>(demo, "_nextCycleId"));
    }

    [Fact]
    public void AppendCycleLog_appends_a_line_to_the_cycle_log()
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
