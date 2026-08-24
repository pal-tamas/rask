using Rask.Example.Shared.Tests.Infrastructure;

namespace Rask.Example.Shared.Tests.Demos;

public sealed partial class CancellationProbeTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task OnMountAsync_LogsCancelledOnUnmount_ThroughRegisterCallback()
    {
        var log = new LifecycleLog();
        var mounted = true;
        var page = RaskTest.Render(
            () => mounted ? CancellationProbe.Log(log.Add).InstanceId(1) : null,
            TestServices.Default());
        // Wait until the probe is in the "running" state (its post-StateHasChanged render).
        await WaitFor.True(() => page.Render().Contains("running"), TimeSpan.FromSeconds(2));

        mounted = false;
        page.Render();

        await WaitFor.True(() => log.Contains("cancelled"), TimeSpan.FromSeconds(2));
        Assert.Contains(log.Snapshot(), e => e.Contains("#1 cancelled"));
    }

    [Fact]
    public async Task OnMountAsync_DoubleObservation_LogsOnceViaInterlocked()
    {
        var log = new LifecycleLog();
        var mounted = true;
        var page = RaskTest.Render(
            () => mounted ? CancellationProbe.Log(log.Add).InstanceId(9) : null,
            TestServices.Default());
        await WaitFor.True(() => page.Render().Contains("running"), TimeSpan.FromSeconds(2));

        mounted = false;
        page.Render();
        await WaitFor.True(() => log.Contains("cancelled"), TimeSpan.FromSeconds(2));

        // Even if both the Register callback and the polling loop observe cancellation,
        // Interlocked.Exchange guards a single "cancelled" log entry.
        var cancelledEntries = log.Snapshot().Count(e => e.Contains("#9 cancelled"));
        Assert.Equal(1, cancelledEntries);
    }
}
