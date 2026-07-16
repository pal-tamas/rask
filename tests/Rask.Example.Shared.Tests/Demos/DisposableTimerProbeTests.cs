using Rask.Example.Shared.Tests.Infrastructure;
using static Rask.Example.Shared.Features.Generated;
using static Rask.Example.Shared.Generated;

namespace Rask.Example.Shared.Tests.Demos;

public sealed class DisposableTimerProbeTests
{
    [Fact]
    public async Task DisposableTimerProbe_DisposeFires_OnUnmount()
    {
        var log = new LifecycleLog();
        var mounted = true;
        var page = RaskTest.Render(
            () => mounted ? DisposableTimerProbe(log.Add, 1) : null,
            TestServices.Default());
        Assert.Contains(log.Snapshot(), e => e == "#1 mounted");

        mounted = false;
        page.Render();
        await WaitFor.True(() => log.Contains("disposed"), TimeSpan.FromSeconds(2));

        Assert.Contains(log.Snapshot(), e => e.StartsWith("#1 disposed"));
    }

    [Fact]
    public async Task UnmountTimerProbe_TimerStoppedOnUnmount_NoFurtherTicks()
    {
        var log = new LifecycleLog();
        var mounted = true;
        var page = RaskTest.Render(
            () => mounted ? UnmountTimerProbe(log.Add, 2) : null,
            TestServices.Default());
        Assert.Contains(log.Snapshot(), e => e == "#2 ticker started");

        mounted = false;
        page.Render();
        await WaitFor.True(() => log.Contains("ticker stopped"), TimeSpan.FromSeconds(2));

        Assert.Contains(log.Snapshot(), e => e.StartsWith("#2 ticker stopped after"));
    }

    [Fact]
    public async Task DisposableAsyncProbe_DisposeAsyncFires_OnUnmount()
    {
        var log = new LifecycleLog();
        var mounted = true;
        var page = RaskTest.Render(
            () => mounted ? DisposableAsyncProbe(log.Add, 3) : null,
            TestServices.Default());
        Assert.Contains(log.Snapshot(), e => e == "#3 async-mounted");

        mounted = false;
        page.Render();
        await WaitFor.True(() => log.Contains("async-disposed"), TimeSpan.FromSeconds(2));

        Assert.Contains(log.Snapshot(), e => e.StartsWith("#3 async-disposed"));
    }
}
