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
        var host = new LiveHost(
            () => DisposableTimerProbe(log.Add, 1),
            TestServices.Default());

        host.RenderAsLiveRoot();
        Assert.Contains(log.Snapshot(), e => e == "#1 mounted");

        host.Mounted = false;
        host.RenderAsLiveRoot();
        await WaitFor.True(() => log.Contains("disposed"), TimeSpan.FromSeconds(2));

        Assert.Contains(log.Snapshot(), e => e.StartsWith("#1 disposed"));
    }

    [Fact]
    public async Task UnmountTimerProbe_TimerStoppedOnUnmount_NoFurtherTicks()
    {
        var log = new LifecycleLog();
        var host = new LiveHost(
            () => UnmountTimerProbe(log.Add, 2),
            TestServices.Default());

        host.RenderAsLiveRoot();
        Assert.Contains(log.Snapshot(), e => e == "#2 ticker started");

        host.Mounted = false;
        host.RenderAsLiveRoot();
        await WaitFor.True(() => log.Contains("ticker stopped"), TimeSpan.FromSeconds(2));

        Assert.Contains(log.Snapshot(), e => e.StartsWith("#2 ticker stopped after"));
    }

    [Fact]
    public async Task DisposableAsyncProbe_DisposeAsyncFires_OnUnmount()
    {
        var log = new LifecycleLog();
        var host = new LiveHost(
            () => DisposableAsyncProbe(log.Add, 3),
            TestServices.Default());

        host.RenderAsLiveRoot();
        Assert.Contains(log.Snapshot(), e => e == "#3 async-mounted");

        host.Mounted = false;
        host.RenderAsLiveRoot();
        await WaitFor.True(() => log.Contains("async-disposed"), TimeSpan.FromSeconds(2));

        Assert.Contains(log.Snapshot(), e => e.StartsWith("#3 async-disposed"));
    }
}
