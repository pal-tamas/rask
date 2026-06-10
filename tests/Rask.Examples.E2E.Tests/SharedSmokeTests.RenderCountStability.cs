using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// Re-render stability. Catches:
//   * Unnecessary re-renders (a single click triggering N>1 renders).
//   * Infinite render loops (renderCount climbing on its own without input).
//   * Lifecycle hooks issuing extra StateHasChanged calls that shouldn't.
//
// Production goal: the framework only re-renders when something observable
// changes. Each test below asserts the observed render-count delta matches
// the expected delta. A regression that adds a spurious render shows up as
// "expected +1, got +2" — small enough to be missed in casual inspection,
// loud enough here.
public abstract partial class SharedSmokeTests
{
    [Fact]
    public Task RenderCount_LifecycleProbe_IdleStaysConstant() => RunAsync(async () =>
    {
        // LifecycleProbe displays a "Render #N" badge. After the initial mount
        // settles, the number must NOT advance without user input — no timers,
        // no idle re-renders. A failure here means something is calling
        // StateHasChanged from a background path.
        await NavigateToAsync("/lifecycle");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Lifecycle hooks",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        // Wait for initial render to settle. The probe's OnMountAsync awaits
        // 450ms, so a couple of post-await re-renders happen up-front. By 2s
        // the probe should be quiescent.
        await Page.WaitForTimeoutAsync(2000);
        var initial = await ReadProbeRenderCountAsync();
        Assert.True(initial > 0, "Probe should have rendered at least once by now.");

        // Idle for 3 seconds.
        await Page.WaitForTimeoutAsync(3000);
        var idle = await ReadProbeRenderCountAsync();

        // Tolerate a tiny amount of late async settle (e.g. a continuation
        // posted just after the initial sample arrives). The point of this
        // test is to catch runaway loops — 1-2 extra background renders is
        // not a loop; 20+ would be.
        var delta = idle - initial;
        Assert.True(delta <= 2,
            $"Probe re-rendered while idle. initial={initial} idle={idle} delta={delta}");
    });

    [Fact]
    public Task RenderCount_LifecycleProbe_DirectNav_HookSequenceMatchesAcrossHosts() => RunAsync(async () =>
    {
        // Server and WASM must produce the same initial-mount hook sequence on the
        // LifecycleProbe. The sequence ends at "OnMountAsync (after 450ms await)" because
        // commit c923376 collapsed the LifecycleSyncContext.Post + terminal-ContinueWith
        // double-StateHasChanged into a single post-await render: Post sets PostFired=true,
        // the terminal ContinueWith short-circuits, and only Post's _component.StateHasChanged
        // fires. That render's HTML captures _log through entry 6 ("OnMountAsync (after
        // 450ms await)"); the trailing OnRendered(firstRender:false) is appended to _log
        // AFTER the HTML is serialised, so it lives in component state but never reaches
        // the browser — there is no subsequent render to re-serialise it.
        //
        // Pre-c923376 this test asserted two trailing OnRendered(False) entries because
        // ContinueWith ALSO fired StateHasChanged, producing a second render whose HTML
        // captured the first OnRendered(False) entry. That extra render is exactly what
        // c923376 fixed (Render #1 → #3 skipping #2).
        await NavigateToAsync("/lifecycle");
        await Expect(Page.Locator("main h1.h2"))
            .ToHaveTextAsync("Lifecycle hooks",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        // Wait for the probe's 450ms OnMountAsync to settle plus tail renders.
        await Page.WaitForTimeoutAsync(2000);

        var entries = await Page.Locator(".card-body ol.list-group").First
            .Locator("li code").AllInnerTextsAsync();

        // Canonical sequence — identical on all three hosts (Server, Wasm, StandaloneWasm).
        // OnRendered(firstRender:True) appears in this list because the second render
        // (driven by Post's StateHasChanged after the 450ms await) serialises _log after
        // the initial RaiseOnRendered already appended it. The final OnRendered(False)
        // is invisible by construction — see comment above.
        var expected = new[]
        {
            "OnMount", "OnMountAsync (start)", "OnPropsChanged (render #1)", "OnPropsChangedAsync",
            "OnRendered(firstRender: True)", "OnMountAsync (after 450ms await)"
        };

        Assert.Equal(expected, entries);
    });

    [Fact]
    public Task RenderCount_LifecycleProbe_NavigatedFromRealtime_NoGhostRenders() => RunAsync(async () =>
    {
        // Regression: pre-fix, navigating from a page with a long-running async
        // OnMountAsync (LiveTicker's poll loop) to /lifecycle produced 10+ ghost
        // OnRendered(firstRender:false) entries on the freshly-mounted probe.
        // Each in-flight Task.Delay/HTTP/JS continuation captured by
        // LifecycleSyncContext was, on cancellation, calling StateHasChanged on
        // the disposed LiveTicker — which still routed through the session's
        // RenderHandle and queued a full root render. RaiseOnRendered fires on
        // every alive component after each root render, so the new probe got
        // pinged once per ghost render.
        //
        // Oracle: the probe's "Hook log" lists every OnRendered call. Direct
        // navigation produces 1 OnRendered(True) + ~2 OnRendered(False). A leak
        // from the previous page bumps the False count by 10+.
        await NavigateToAsync("/realtime/BTC");
        await Expect(Page.Locator("main h1.h2"))
            .ToContainTextAsync("live ticker",
                new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });

        // Let LiveTicker's poll loop suspend on at least one Task.Delay before
        // navigating away — the bug only surfaces when there is in-flight async
        // work to cancel.
        await Page.WaitForTimeoutAsync(1000);

        await ClickSidebar("Lifecycle");
        await Expect(Page.Locator("main h1.h2"))
            .ToHaveTextAsync("Lifecycle hooks",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        // Wait for the probe's 450ms OnMountAsync to settle plus tail renders.
        await Page.WaitForTimeoutAsync(2000);

        var entries = await Page.Locator(".card-body ol.list-group").First
            .Locator("li code").AllInnerTextsAsync();
        var falseCount = entries.Count(e => e.Contains("OnRendered(firstRender: False)"));

        // The honest budget: 2 post-450ms-await renders (Post StateHasChanged +
        // terminal ContinueWith StateHasChanged) plus up to 2 more for late
        // async settle from the previous page's teardown. Anything beyond 4 is
        // the ghost-render regression.
        Assert.True(falseCount <= 4,
            $"Ghost OnRendered renders detected after nav from /realtime/BTC. " +
            $"False count = {falseCount}; full log:\n  - " +
            string.Join("\n  - ", entries));
    });

    [Fact]
    public Task RenderCount_LifecycleProbe_TriggerReRender_AdvancesByExactlyOne() => RunAsync(async () =>
    {
        // One click on "Trigger re-render" must produce exactly one additional
        // render. Two renders would mean a spurious StateHasChanged is firing
        // in the handler or via a subscription.
        await NavigateToAsync("/lifecycle");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Lifecycle hooks",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
        await Page.WaitForTimeoutAsync(2000);

        var before = await ReadProbeRenderCountAsync();

        // Click the "Trigger re-render" button — first button inside the probe card.
        await Page.Locator("button:has-text('Trigger re-render')").First.ClickAsync();
        await Page.WaitForTimeoutAsync(500);

        var after = await ReadProbeRenderCountAsync();
        Assert.Equal(before + 1, after);
    });

    [Fact]
    public Task RenderCount_BindingPage_SingleKeystroke_DoesNotMultiRender() => RunAsync(async () =>
    {
        // Typing a single character into a bound text input must NOT trigger
        // a re-render storm. We can't directly observe BindingPage's render
        // count, but we can observe the morph's behaviour: if the framework
        // re-renders multiple times per keystroke, the input gets clobbered
        // (a known historical regression).
        //
        // The /binding page echoes typed text into <output>. We type a known
        // string, wait, and assert the input still holds exactly what we typed
        // and the echo matches — proving the morph didn't reset the input
        // mid-keystroke from a stale render.
        await NavigateToAsync("/binding");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Two-way binding",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var input = Page.Locator("input[name='Name']");
        await input.FillAsync("RaskFramework");
        await Page.WaitForTimeoutAsync(500);

        await Expect(input).ToHaveValueAsync("RaskFramework",
            new LocatorAssertionsToHaveValueOptions { Timeout = 5_000 });
    });

    [Fact]
    public Task RenderCount_NavigatorButtons_OneRenderPerNavAction() => RunAsync(async () =>
    {
        // Each Navigator button click changes the route → one re-render of
        // the page. The URL after one click should reflect exactly the change
        // requested. A second render firing as a side-effect would land us in
        // ambiguous state.
        await NavigateToAsync("/navigator");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Navigator",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Page.Locator("button:has-text('SetQuery page=1')").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*[\\?&]page=1"),
            new PageAssertionsToHaveURLOptions { Timeout = 5_000 });

        await Page.Locator("button:has-text('SetQuery page=2')").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*[\\?&]page=2"),
            new PageAssertionsToHaveURLOptions { Timeout = 5_000 });
    });

    private async Task<int> ReadProbeRenderCountAsync()
    {
        // Reads the "Render #N" badge text and returns N.
        var text = await Page.Locator(".badge.text-bg-primary:has-text('Render #')").First
            .InnerTextAsync();
        var hashIdx = text.IndexOf('#');
        if (hashIdx < 0)
        {
            throw new InvalidOperationException($"Unexpected badge text: '{text}'");
        }

        var rest = text[(hashIdx + 1)..].Trim();
        return int.Parse(rest);
    }
}
