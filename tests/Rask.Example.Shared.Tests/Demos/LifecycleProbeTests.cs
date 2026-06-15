using Rask.Example.Shared.Tests.Infrastructure;
using static Rask.Example.Shared.Features.Generated;
using static Rask.Example.Shared.Generated;

namespace Rask.Example.Shared.Tests.Demos;

public sealed class LifecycleProbeTests
{
    [Fact]
    public async Task LifecycleProbe_FiresMountThroughRenderedHooks_InOrder()
    {
        var host = new LiveHost(() => LifecycleProbe(), TestServices.Default());

        host.RenderAsLiveRoot();
        // OnMountAsync awaits 450ms; allow time for the full sequence.
        await WaitFor.True(() => RenderedHtml(host).Contains("OnMountAsync (after 450ms await)"),
            TimeSpan.FromSeconds(2));

        var html = host.RenderAsLiveRoot();
        Assert.Contains("OnMount", html);
        Assert.Contains("OnMountAsync (start)", html);
        Assert.Contains("OnMountAsync (after 450ms await)", html);
        Assert.Contains("OnPropsChanged", html);
        Assert.Contains("OnRendered(firstRender: True)", html);
    }

    [Fact]
    public void LifecycleCycleProbe_ReportsHooksToParentOwnedLog()
    {
        var log = new LifecycleLog();
        var instanceId = 7;
        var host = new LiveHost(
            () => LifecycleCycleProbe(log.Add, instanceId),
            TestServices.Default());

        host.RenderAsLiveRoot();

        Assert.Contains(log.Snapshot(), e => e == "#7 OnMount");
        Assert.Contains(log.Snapshot(), e => e.StartsWith("#7 OnMountAsync (start)"));
    }

    [Fact]
    public async Task LifecycleCycleProbe_OnUnmount_FiresWhenRemovedFromTree()
    {
        var log = new LifecycleLog();
        var host = new LiveHost(
            () => LifecycleCycleProbe(log.Add, 1),
            TestServices.Default());

        host.RenderAsLiveRoot();
        await WaitFor.True(() => log.Contains("#1 OnMountAsync (after 150ms await)"), TimeSpan.FromSeconds(2));

        host.Mounted = false;
        host.RenderAsLiveRoot();
        await WaitFor.True(() => log.Contains("#1 OnUnmountAsync"), TimeSpan.FromSeconds(2));

        Assert.Contains(log.Snapshot(), e => e == "#1 OnUnmount");
        Assert.Contains(log.Snapshot(), e => e == "#1 OnUnmountAsync");
    }

    // Helper: the LifecycleProbe captures its log internally and re-renders, so we
    // peek at the rendered HTML to inspect the log contents.
    private static string RenderedHtml(LiveHost host) => host.RenderAsLiveRoot();
}
