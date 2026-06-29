using System.Text.Json;
using Rask.Example.Shared.Tests.Infrastructure;
using static Rask.Example.Shared.Features.Generated;
using static Rask.Example.Shared.Generated;

namespace Rask.Example.Shared.Tests.Demos;

public sealed class LifecycleProbeTests
{
    // Regression: the "Trigger re-render" button is a BsButton, which forwards its OnClick down to the
    // native <button>. The handler closes over the probe (appends to its hook log), so firing it re-renders
    // the owning probe and the new entry shows up in the repaint. An earlier empty `() => {}` handler was a
    // static delegate (null Target): AutoCallback.Wrap left it unwrapped and the live runtime fell back to
    // the element's render-owner — BsButton, not the probe — so the probe never repainted and the
    // WalksEveryPage E2E journey failed on the render-counter assertion.
    [Fact]
    public async Task TriggerReRender_ThroughBsButton_RunsHandlerAndRepaintsProbe()
    {
        var host = new LiveHost(() => LifecycleProbe(), TestServices.Default());

        // The probe's only click handler is the trigger button; that an id exists proves BsButton forwarded
        // the OnClick to the native button.
        var clickId = ClickIds(host.RenderAsLiveRoot())[0];
        await host.TryInvokeHandlerAsync(clickId, Empty());

        Assert.Contains("Trigger re-render (button click)", host.RenderAsLiveRoot());
    }


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

    private static JsonElement Empty()
    {
        using var doc = JsonDocument.Parse("{}");
        return doc.RootElement.Clone();
    }

    private static List<string> ClickIds(string html)
    {
        var ids = new List<string>();
        const string marker = "data-rask-on-click=\"";
        var i = 0;
        while ((i = html.IndexOf(marker, i, StringComparison.Ordinal)) >= 0)
        {
            i += marker.Length;
            var end = html.IndexOf('"', i);
            ids.Add(html[i..end]);
            i = end;
        }

        return ids;
    }
}
