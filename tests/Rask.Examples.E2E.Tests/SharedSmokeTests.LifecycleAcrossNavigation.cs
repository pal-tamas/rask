using Microsoft.Playwright;
using Xunit.Sdk;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// Lifecycle correctness — tests that work across all three hosts (Server,
// Wasm.Host, StandaloneWasm). The LiveTicker-based subset lives in
// ExampleSmokeTests.LifecycleAcrossNavigation.cs because StandaloneWasm's
// NavigateToAsync waits for the URL to match the target path, which is
// timing-flaky for /realtime/{Symbol} on WasmAppHost (same reason the prior
// LiveTicker tests already lived in ExampleSmokeTests).
public abstract partial class SharedSmokeTests
{
    [Fact]
    public Task Lifecycle_CancellationToken_CancelsLongDelayOnUnmount() => RunAsync(async () =>
    {
        // Goes directly at the CancellationToken contract via the CancellationPage's
        // dedicated probe. The probe explicitly captures the lifetime token and
        // polls every 100ms — so a successful cancellation appears as a
        // "cancelled (X ms)" log entry within the 2.5s window, NOT a "completed"
        // entry. This test verifies the ELAPSED time recorded by the probe is
        // well under 2.5s, proving the cancellation happened promptly (not just
        // that the loop happened to notice eventually).
        await NavigateToAsync("/cancellation");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Cancellation",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Page.Locator("#cancel-mount").ClickAsync();
        await Expect(Page.Locator(".cancel-probe-pill"))
            .ToContainTextAsync("running",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Wait ~300ms so the probe has progressed meaningfully into the poll loop,
        // then unmount. The "cancelled (X ms)" entry's X must be less than 2500
        // (the full delay), proving the await actually observed the cancellation
        // rather than running to completion.
        await Page.WaitForTimeoutAsync(300);
        await Page.Locator("#cancel-unmount").ClickAsync();

        var log = Page.Locator(".cancel-log");
        await Expect(log).ToContainTextAsync("cancelled",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        var text = await log.InnerTextAsync();
        // Parse out the ms number: "#N cancelled (123 ms)"
        var idx = text.IndexOf("cancelled (", StringComparison.Ordinal);
        Assert.True(idx >= 0, $"Cancel log did not contain elapsed ms. Log:\n{text}");
        var tail = text.Substring(idx + "cancelled (".Length);
        var endIdx = tail.IndexOf(" ms", StringComparison.Ordinal);
        Assert.True(endIdx > 0, $"Cancel log elapsed marker malformed:\n{text}");
        var msStr = tail.Substring(0, endIdx);
        var ms = int.Parse(msStr);
        Assert.True(ms < 2500,
            $"Cancellation should have fired before the 2500ms delay completed, but elapsed = {ms} ms.\nLog:\n{text}");
    });

    [Fact]
    public Task Lifecycle_LifecyclePage_MountUnmountCycle_LogsAllHooks() => RunAsync(async () =>
    {
        // LifecyclePage's "Mount / unmount cycle" card uses LifecycleCycleProbe
        // — a probe that logs every hook (mount + unmount) into a PAGE-owned
        // list so the unmount entries survive the probe being torn down. This
        // is the only place in the showcase that exposes unmount entries via
        // the DOM rather than via side-effects, making it a perfect oracle for
        // the OnUnmount + OnUnmountAsync contract.
        await NavigateToAsync("/lifecycle");
        await Expect(Page.Locator("main h1.h2"))
            .ToHaveTextAsync("Lifecycle hooks",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Page.Locator("#lifecycle-cycle-mount").ClickAsync();
        await Expect(Page.Locator("#lifecycle-cycle-log")).ToContainTextAsync("OnMount",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Expect(Page.Locator("#lifecycle-cycle-log")).ToContainTextAsync(
            "OnMountAsync (after 150ms await)",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        await Page.Locator("#lifecycle-cycle-unmount").ClickAsync();
        await Expect(Page.Locator("#lifecycle-cycle-log")).ToContainTextAsync("OnUnmount",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Expect(Page.Locator("#lifecycle-cycle-log")).ToContainTextAsync("OnUnmountAsync",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Order assertion: mount entries appear before unmount entries.
        var combined = string.Join("\n", await Page.Locator("#lifecycle-cycle-log").AllInnerTextsAsync());
        var mountIdx = combined.IndexOf("OnMount", StringComparison.Ordinal);
        var unmountIdx = combined.IndexOf("OnUnmount", StringComparison.Ordinal);
        Assert.True(mountIdx >= 0 && unmountIdx > mountIdx,
            $"Mount/unmount order broken. mount={mountIdx} unmount={unmountIdx}.\nLog:\n{combined}");
    });

    [Fact]
    public Task Lifecycle_RemountAfterUnmount_FiresFreshMountSequence() => RunAsync(async () =>
    {
        // After unmounting, remounting the probe must produce a SECOND OnMount
        // entry tagged with a different instance id. This proves the framework
        // didn't cache the disposed instance — a regression that did this would
        // silently reuse the dead component with its cancelled CancellationToken,
        // breaking async hooks on the second mount.
        await NavigateToAsync("/lifecycle");
        await Expect(Page.Locator("main h1.h2"))
            .ToHaveTextAsync("Lifecycle hooks",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Page.Locator("#lifecycle-cycle-mount").ClickAsync();
        await Expect(Page.Locator("#lifecycle-cycle-log")).ToContainTextAsync("#1 OnMount",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        await Page.Locator("#lifecycle-cycle-unmount").ClickAsync();
        await Expect(Page.Locator("#lifecycle-cycle-log")).ToContainTextAsync("#1 OnUnmount",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        await Page.Locator("#lifecycle-cycle-mount").ClickAsync();
        await Expect(Page.Locator("#lifecycle-cycle-log")).ToContainTextAsync("#2 OnMount",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        await Expect(Page.Locator("#lifecycle-cycle-log")).ToContainTextAsync(
            "#2 OnMountAsync (after 150ms await)",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    // ---------- helper ----------

    // Poll a predicate until it returns true or the timeout elapses. Used for
    // assertions on values that mutate over time where Playwright's Expect
    // doesn't have a direct matcher.
    private static async Task WaitForAsync(Func<Task<bool>> predicate, int timeoutMs, string message)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new XunitException($"Timed out after {timeoutMs} ms waiting for: {message}");
    }
}
