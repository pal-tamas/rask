using Rask.Site.Tests.Infrastructure;

namespace Rask.Site.Tests.Demos;

public sealed partial class DisposableTimerProbeTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task DisposableTimerProbe_fires_dispose_on_unmount()
    {
        var log = new LifecycleLog();
        var mounted = true;
        var page = Page.Render(
            () => mounted ? DisposableTimerProbe.InstanceId(1).Log(log.Add) : null,
            TestServices.Default());
        Assert.Contains(log.Snapshot(), e => e == "#1 mounted");

        mounted = false;
        page.Render();
        await WaitFor.True(() => log.Contains("disposed"));

        Assert.Contains(log.Snapshot(), e => e.StartsWith("#1 disposed"));
    }

    [Fact]
    public async Task UnmountTimerProbe_stops_its_timer_on_unmount_with_no_further_ticks()
    {
        var log = new LifecycleLog();
        var mounted = true;
        var page = Page.Render(
            () => mounted ? UnmountTimerProbe.InstanceId(2).Log(log.Add) : null,
            TestServices.Default());
        Assert.Contains(log.Snapshot(), e => e == "#2 ticker started");

        mounted = false;
        page.Render();
        await WaitFor.True(() => log.Contains("ticker stopped"));

        Assert.Contains(log.Snapshot(), e => e.StartsWith("#2 ticker stopped after"));
    }

    [Fact]
    public async Task DisposableAsyncProbe_fires_DisposeAsync_on_unmount()
    {
        var log = new LifecycleLog();
        var mounted = true;
        var page = Page.Render(
            () => mounted ? DisposableAsyncProbe.InstanceId(3).Log(log.Add) : null,
            TestServices.Default());
        Assert.Contains(log.Snapshot(), e => e == "#3 async-mounted");

        mounted = false;
        page.Render();
        await WaitFor.True(() => log.Contains("async-disposed"));

        Assert.Contains(log.Snapshot(), e => e.StartsWith("#3 async-disposed"));
    }
}
