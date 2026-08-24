using System.Text.Json;
using Rask.Example.Shared.Tests.Infrastructure;

namespace Rask.Example.Shared.Tests.Demos;

public sealed partial class LifecycleProbeTests : global::Rask.Core.RaskMarkup
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
        var page = RaskTest.Render(() => LifecycleProbe, TestServices.Default());

        // The probe's only click handler is the trigger button; that an id exists proves BsButton forwarded
        // the OnClick to the native button.
        var clickId = Markup.Attrs(page.Render(), "data-rask-on-click")[0];
        await page.InvokeAsync(clickId);

        Assert.Contains("Trigger re-render (button click)", page.Render());
    }


    [Fact]
    public async Task LifecycleProbe_FiresMountThroughRenderedHooks_InOrder()
    {
        var page = RaskTest.Render(() => LifecycleProbe, TestServices.Default());
        // OnMountAsync awaits 450ms; allow time for the full sequence.
        await WaitFor.True(() => page.Render().Contains("OnMountAsync (after 450ms await)"),
            TimeSpan.FromSeconds(2));

        var html = page.Render();
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
        var page = RaskTest.Render(
            () => LifecycleCycleProbe.Log(log.Add).InstanceId(instanceId),
            TestServices.Default());

        Assert.Contains(log.Snapshot(), e => e == "#7 OnMount");
        Assert.Contains(log.Snapshot(), e => e.StartsWith("#7 OnMountAsync (start)"));
    }

    [Fact]
    public async Task LifecycleCycleProbe_OnUnmount_FiresWhenRemovedFromTree()
    {
        var log = new LifecycleLog();
        var mounted = true;
        var page = RaskTest.Render(
            () => mounted ? LifecycleCycleProbe.Log(log.Add).InstanceId(1) : null,
            TestServices.Default());
        await WaitFor.True(() => log.Contains("#1 OnMountAsync (after 150ms await)"), TimeSpan.FromSeconds(2));

        mounted = false;
        page.Render();
        await WaitFor.True(() => log.Contains("#1 OnUnmountAsync"), TimeSpan.FromSeconds(2));

        Assert.Contains(log.Snapshot(), e => e == "#1 OnUnmount");
        Assert.Contains(log.Snapshot(), e => e == "#1 OnUnmountAsync");
    }
}
