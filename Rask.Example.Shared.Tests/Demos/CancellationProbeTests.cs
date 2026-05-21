using Rask.Core;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Tests.Infrastructure;
using static Rask.Example.Shared.Demos.Components;

namespace Rask.Example.Shared.Tests.Demos;

public sealed class CancellationProbeTests
{
    [Fact]
    public async Task OnMountAsync_LogsCancelledOnUnmount_ThroughRegisterCallback()
    {
        var log = new LifecycleLog();
        var host = new LiveHost(
            () => CancellationProbe(Log: log.Add, InstanceId: 1),
            TestServices.Default());

        host.RenderAsLiveRoot();
        // Wait until the probe is in the "running" state (its post-StateHasChanged render).
        await WaitFor.True(() => host.RenderAsLiveRoot().Contains("running"), TimeSpan.FromSeconds(2));

        host.Mounted = false;
        host.RenderAsLiveRoot();

        await WaitFor.True(() => log.Contains("cancelled"), TimeSpan.FromSeconds(2));
        Assert.Contains(log.Snapshot(), e => e.Contains("#1 cancelled"));
    }

    [Fact]
    public async Task OnMountAsync_DoubleObservation_LogsOnceViaInterlocked()
    {
        var log = new LifecycleLog();
        var host = new LiveHost(
            () => CancellationProbe(Log: log.Add, InstanceId: 9),
            TestServices.Default());

        host.RenderAsLiveRoot();
        await WaitFor.True(() => host.RenderAsLiveRoot().Contains("running"), TimeSpan.FromSeconds(2));

        host.Mounted = false;
        host.RenderAsLiveRoot();
        await WaitFor.True(() => log.Contains("cancelled"), TimeSpan.FromSeconds(2));

        // Even if both the Register callback and the polling loop observe cancellation,
        // Interlocked.Exchange guards a single "cancelled" log entry.
        var cancelledEntries = log.Snapshot().Count(e => e.Contains("#9 cancelled"));
        Assert.Equal(1, cancelledEntries);
    }
}
