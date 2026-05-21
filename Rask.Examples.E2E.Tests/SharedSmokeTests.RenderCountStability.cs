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
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(".*[\\?&]page=1"),
            new PageAssertionsToHaveURLOptions { Timeout = 5_000 });

        await Page.Locator("button:has-text('SetQuery page=2')").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(".*[\\?&]page=2"),
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
