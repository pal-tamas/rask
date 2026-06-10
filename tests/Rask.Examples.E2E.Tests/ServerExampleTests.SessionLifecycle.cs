using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// Server-only session lifecycle tests:
//   * Multiple concurrent sessions are isolated (state in tab A doesn't bleed
//     into tab B).
//   * Reconnect within the SessionGracePeriod (30s) reuses the same session
//     instance — state preserved.
//   * After the grace period elapses with no reconnect, the session is
//     disposed and its memory freed (GC heap returns to baseline).
//   * No reconnect at all leaves no dangling tasks (poll loops, JS interop
//     waiters) — heap and session count both drop.
//
// These are framework guarantees that production deployments rely on. A
// regression here = leaked WebSocket handlers, leaked component trees, or
// crossed-wires between user sessions.
public sealed partial class ServerExampleTests
{
    [Fact]
    public async Task SessionLifecycle_MultipleConcurrentBrowsers_StateIsolated()
    {
        try
        {
            // Two independent browser contexts = two independent WS sessions
            // (different data-rask-root ids, different LiveSession instances on
            // the server). Clicking the counter in context A must not affect
            // the counter in context B.
            await using var ctxA = await Page.Context.Browser!.NewContextAsync(
                new BrowserNewContextOptions { BaseURL = BaseUrl });
            await using var ctxB = await Page.Context.Browser!.NewContextAsync(
                new BrowserNewContextOptions { BaseURL = BaseUrl });

            var pageA = await ctxA.NewPageAsync();
            var pageB = await ctxB.NewPageAsync();
            await pageA.GotoAsync("/events");
            await pageB.GotoAsync("/events");

            await Expect(pageA.Locator("main h1.h2")).ToHaveTextAsync("Events",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
            await Expect(pageB.Locator("main h1.h2")).ToHaveTextAsync("Events",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

            var btnA = pageA.Locator(".sample-result-body button:has-text('Clicks:')").First;
            var btnB = pageB.Locator(".sample-result-body button:has-text('Clicks:')").First;

            await btnA.ClickAsync();
            await btnA.ClickAsync();
            await btnA.ClickAsync();
            await Expect(btnA).ToContainTextAsync("Clicks: 3",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

            // Context B must still show 0 clicks — no cross-session leakage.
            await Expect(btnB).ToContainTextAsync("Clicks: 0",
                new LocatorAssertionsToContainTextOptions { Timeout = 5_000 });
        }
        finally
        {
            await TestArtifacts.DumpAsync(Page, FixtureName,
                nameof(SessionLifecycle_MultipleConcurrentBrowsers_StateIsolated), ServerLog);
        }
    }

    [Fact]
    public async Task SessionLifecycle_ReconnectWithinGrace_ReusesSession()
    {
        try
        {
            // Toggle offline/online quickly (well under the 30s grace period).
            // The session must be reused — click counter retains its value
            // across the reconnect.
            await Page.GotoAsync("/events");
            await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Events",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

            var clickButton = Page.Locator(".sample-result-body button:has-text('Clicks:')").First;
            await clickButton.ClickAsync();
            await clickButton.ClickAsync();
            await Expect(clickButton).ToContainTextAsync("Clicks: 2",
                new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

            // Capture the session id from the DOM before going offline. The
            // client picks it up from data-rask-root and resends it in the
            // hello frame after reconnect.
            var sidBefore = await Page.Locator("[data-rask-root]").First.GetAttributeAsync("data-rask-root");

            await Page.Context.SetOfflineAsync(true);
            await Page.WaitForTimeoutAsync(2000);
            await Page.Context.SetOfflineAsync(false);

            // Click once more — if the session was reused, this lands as click #3.
            // If a new session was issued, the counter would reset to 1.
            await clickButton.ClickAsync();
            await Expect(clickButton).ToContainTextAsync("Clicks: 3",
                new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });

            var sidAfter = await Page.Locator("[data-rask-root]").First.GetAttributeAsync("data-rask-root");
            Assert.Equal(sidBefore, sidAfter);
        }
        finally
        {
            await TestArtifacts.DumpAsync(Page, FixtureName,
                nameof(SessionLifecycle_ReconnectWithinGrace_ReusesSession), ServerLog);
        }
    }

    [Fact]
    public async Task SessionLifecycle_NoReconnectBeyondGrace_SessionDisposed()
    {
        try
        {
            // Stay offline past the 30s SessionGracePeriod. The server schedules
            // the disposal at the moment of disconnect; after the grace elapses,
            // this test's session has been disposed.
            //
            // Earlier tests in the same collection may have left sessions still
            // inside their grace window — those become noise. We assert on the
            // DELTA caused by THIS test (session created then cleaned up), not
            // the absolute count.

            using var http = new HttpClient { BaseAddress = new Uri(BaseUrl) };

            // Use a dedicated browser context so closing it fully tears down
            // the WebSocket — Page.Context.SetOfflineAsync alone doesn't force
            // existing sockets to close on all platforms.
            await using var ctx = await Page.Context.Browser!.NewContextAsync(
                new BrowserNewContextOptions { BaseURL = BaseUrl });
            var page = await ctx.NewPageAsync();
            await page.GotoAsync("/events");
            await Expect(page.Locator("main h1.h2")).ToHaveTextAsync("Events",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

            var afterOpen = await GetDiagAsync(http);

            // Closing the context drops the WS — the server's socket loop hits
            // the finally and schedules removal in SessionGracePeriod.
            await ctx.CloseAsync();

            // Wait through the grace period plus a buffer for the scheduled
            // removal task to actually fire.
            await Task.Delay(33_000);

            var afterGrace = await GetDiagAsync(http);

            // Strict delta: opening a page added at least one session; after
            // the grace period at least one session is gone. Captures the
            // disposal we care about without coupling to whatever leftovers
            // earlier tests parked in the store.
            Assert.True(afterGrace.Sessions < afterOpen.Sessions,
                $"Session count should drop after grace. afterOpen={afterOpen.Sessions} afterGrace={afterGrace.Sessions}");
        }
        finally
        {
            await TestArtifacts.DumpAsync(Page, FixtureName,
                nameof(SessionLifecycle_NoReconnectBeyondGrace_SessionDisposed), ServerLog);
        }
    }

    [Fact]
    public async Task SessionLifecycle_StressManySessions_HeapAndSessionCountBoundedAfterCleanup()
    {
        try
        {
            // Open + close 20 short-lived sessions. The .NET-side GC heap and
            // session count must both return close to their starting baseline.
            // A leak — uncancelled poll loops, captured `this` in event handlers,
            // dangling RaskJSRuntime references — would manifest as unbounded
            // growth in either number.
            using var http = new HttpClient { BaseAddress = new Uri(BaseUrl) };
            var baseline = await GetDiagAsync(http);

            const int sessionCount = 20;
            for (var i = 0; i < sessionCount; i++)
            {
                await using var ctx = await Page.Context.Browser!.NewContextAsync(
                    new BrowserNewContextOptions { BaseURL = BaseUrl });
                var p = await ctx.NewPageAsync();
                await p.GotoAsync("/events");
                await Expect(p.Locator("main h1.h2")).ToHaveTextAsync("Events",
                    new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
                await p.CloseAsync();
            }

            // Wait the full grace period + buffer so every scheduled removal fires.
            await Page.WaitForTimeoutAsync(33_000);

            var after = await GetDiagAsync(http);

            // Session count must not have leaked.
            Assert.True(after.Sessions <= baseline.Sessions + 1,
                $"Session count leaked. baseline={baseline.Sessions} after {sessionCount} sessions={after.Sessions}");

            // Heap is allowed to grow by some margin (JIT, intern pool, etc.)
            // but should not be 10x baseline. This is a loose canary — a real
            // leak would be orders of magnitude.
            Assert.True(after.GcMemoryBytes < (baseline.GcMemoryBytes * 10) + 50_000_000,
                $"GC heap suspect growth. baseline={baseline.GcMemoryBytes} after={after.GcMemoryBytes}");
        }
        finally
        {
            await TestArtifacts.DumpAsync(Page, FixtureName,
                nameof(SessionLifecycle_StressManySessions_HeapAndSessionCountBoundedAfterCleanup), ServerLog);
        }
    }

    [Fact]
    public async Task SessionLifecycle_NavigationDoesNotLeakSessions()
    {
        try
        {
            // Navigating within a single page session must not create new server
            // sessions. /events → /binding → /scoped-css → /events: session count
            // must stay constant (the same LiveSession services all of these
            // through the same WS). Earlier tests may have left sessions still
            // in grace — these can clean up during the nav window, so we only
            // assert that nav DID NOT increase the count.
            using var http = new HttpClient { BaseAddress = new Uri(BaseUrl) };

            await Page.GotoAsync("/events");
            await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Events",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
            var afterFirst = await GetDiagAsync(http);

            await ClickSidebar("Two-way binding");
            await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Two-way binding",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

            await ClickSidebar("Scoped CSS");
            await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Scoped CSS",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

            await ClickSidebar("Events");
            await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Events",
                new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });
            var afterReturn = await GetDiagAsync(http);

            Assert.True(afterReturn.Sessions <= afterFirst.Sessions,
                $"Navigation should not create new sessions. afterFirst={afterFirst.Sessions} afterReturn={afterReturn.Sessions}");
        }
        finally
        {
            await TestArtifacts.DumpAsync(Page, FixtureName,
                nameof(SessionLifecycle_NavigationDoesNotLeakSessions), ServerLog);
        }
    }

    private static async Task<DiagSnapshot> GetDiagAsync(HttpClient http)
    {
        // Polls the example server's /_diag endpoint. The endpoint forces a
        // full GC before reporting GcMemoryBytes, so the value reflects live
        // (reachable) memory — not transient allocations.
        var snap = await http.GetFromJsonAsync<DiagSnapshot>("/_diag", DiagJsonContext.Default.DiagSnapshot);
        return snap ?? throw new InvalidOperationException("Empty /_diag response.");
    }

    public sealed record DiagSnapshot(
        [property: JsonPropertyName("sessions")]
        int Sessions,
        [property: JsonPropertyName("gcMemoryBytes")]
        long GcMemoryBytes,
        [property: JsonPropertyName("gen0")] int Gen0,
        [property: JsonPropertyName("gen1")] int Gen1,
        [property: JsonPropertyName("gen2")] int Gen2);

    [JsonSerializable(typeof(DiagSnapshot))]
    private sealed partial class DiagJsonContext : JsonSerializerContext;
}
