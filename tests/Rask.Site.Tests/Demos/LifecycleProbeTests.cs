using Rask.Site.Tests.Infrastructure;

namespace Rask.Site.Tests.Demos;

public sealed partial class LifecycleProbeTests : global::Rask.Core.RaskMarkup
{
    // Regression: the "Trigger re-render" button is a BsButton, which forwards its OnClick down to the
    // native <button>. The handler closes over the probe (appends to its hook log), so firing it re-renders
    // the owning probe and the new entry shows up in the repaint. An earlier empty `() => {}` handler was a
    // static delegate (null Target): AutoCallback.Wrap left it unwrapped and the live runtime fell back to
    // the element's render-owner — BsButton, not the probe — so the probe never repainted and the
    // WalksEveryPage E2E journey failed on the render-counter assertion.
    [Fact]
    public async Task The_trigger_re_render_button_through_BsButton_runs_the_handler_and_repaints_the_probe()
    {
        var page = Page.Render(() => LifecycleProbe, TestServices.Default());

        // The probe's only click handler is the trigger button; that an id exists proves BsButton forwarded
        // the OnClick to the native button.
        var clickId = MarkupAssert.Attrs(page.Render(), "data-rask-on-click")[0];

        // The witness used to be a new line appended to a growing hook log. The probe reports each hook
        // as a FIXED row whose status is text now (#1046 -- a row that appears on a timer makes the
        // demo's markup depend on when it was sampled), so the click's effect is the clicks row moving
        // off "not yet". Same claim, and it pins the count rather than just the presence of a string.
        Assert.Contains("Button clicks", page.Render());
        Assert.Matches(@"Button clicks</code>\s*<span[^>]*>not yet", page.Render());

        await page.InvokeAsync(clickId);

        Assert.Matches(@"Button clicks</code>\s*<span[^>]*>ran 1x", page.Render());
    }

    [Fact]
    public async Task The_LifecycleProbe_fires_its_hooks_from_mount_through_rendered_in_order()
    {
        var page = Page.Render(() => LifecycleProbe, TestServices.Default());

        // Every hook NAME is on screen from the first paint now, so asserting the names alone would
        // pass before a single hook had run. The claim worth making is about each row's STATUS: the
        // awaited row starts pending and becomes resolved, which is the sequence this test is named for.
        var first = page.Render();
        Assert.Matches(@"OnMount \(after a 450ms await\)</code>\s*<span[^>]*>awaiting", first);

        // Mount awaits 450ms; allow time for the full sequence.
        await WaitFor.True(() => page.Render().Contains("resolved"), TimeSpan.FromSeconds(2));

        var html = page.Render();
        Assert.Matches(@"OnMount \(before its await\)</code>\s*<span[^>]*>ran 1x", html);
        Assert.Matches(@"OnMount \(after a 450ms await\)</code>\s*<span[^>]*>resolved", html);
        Assert.Matches(@"OnUpdated</code>\s*<span[^>]*>ran 1x", html);

        // OnFirstRendered runs once however many renders follow; OnRendered runs after every one of them.
        Assert.Matches(@"OnFirstRendered</code>\s*<span[^>]*>ran 1x", html);
        Assert.Matches(@"OnRendered</code>\s*<span[^>]*>ran \d+x", html);
    }

    [Fact]
    public void The_LifecycleCycleProbe_reports_its_hooks_to_the_parent_owned_log()
    {
        var log = new LifecycleLog();
        var instanceId = 7;
        var page = Page.Render(
            () => LifecycleCycleProbe.Log(log.Add).InstanceId(instanceId),
            TestServices.Default());

        Assert.Contains(log.Snapshot(), e => e == "#7 OnMount (before its await)");
    }

    [Fact]
    public async Task The_LifecycleCycleProbe_unmount_fires_when_removed_from_the_tree()
    {
        var log = new LifecycleLog();
        var mounted = true;
        var page = Page.Render(
            () => mounted ? LifecycleCycleProbe.Log(log.Add).InstanceId(1) : null,
            TestServices.Default());
        await WaitFor.True(() => log.Contains("#1 OnMount (after a 150ms await)"), TimeSpan.FromSeconds(2));

        mounted = false;
        page.Render();
        await WaitFor.True(() => log.Contains("#1 OnUnmount"), TimeSpan.FromSeconds(2));

        Assert.Single(log.Snapshot(), e => e == "#1 OnUnmount");
    }
}
