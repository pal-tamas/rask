using System.Runtime.CompilerServices;
using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;

namespace Rask.Examples.E2E.Tests;

// Browser-journey harness shared by every showcase host (Server, Wasm.Host, StandaloneWasm).
// The actual test is a single comprehensive journey per host — see SharedSmokeTests.Journey.cs
// (RunShowcaseJourneyAsync). This file holds only the per-test browser lifecycle, navigation
// primitives, and failure diagnostics that the journey builds on.
//
// Path navigation goes through NavigateToAsync. The default implementation calls
// Page.GotoAsync(path), which works for the ASP.NET hosts that install a SPA fallback.
// StandaloneWasmExampleTests overrides it to home-then-sidebar because WasmAppHost has no
// SPA fallback (deep links 404).
public abstract partial class SharedSmokeTests : IAsyncLifetime
{
    private readonly List<string> _console = new();
    private readonly PlaywrightFixture _pw;
    private IBrowserContext _ctx = default!;

    protected IPage Page = default!;

    protected SharedSmokeTests(PlaywrightFixture pw) => _pw = pw;

    protected abstract string BaseUrl { get; }
    protected abstract string FixtureName { get; }
    protected abstract string ServerLog { get; }

    public async Task InitializeAsync()
    {
        _ctx = await _pw.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            // Grant the browser-gated APIs the /browser showcase exercises so their journey step is
            // deterministic in headless Chromium: clipboard read/write, plus a fixed geolocation fix.
            Permissions = ["clipboard-read", "clipboard-write", "geolocation"],
            Geolocation = new Geolocation { Latitude = 51.5074f, Longitude = -0.1278f, Accuracy = 50 }
        });
        // Record a Playwright trace for the whole journey and keep it only if the journey fails (see
        // RunAsync). The console dump below explains a page that *threw*; it says nothing about a page
        // that is merely never still, which is how #625 presented — a 30s "element is not stable" naming
        // the element and nothing about what was moving. A trace's DOM snapshots do name it.
        //
        // Always-on rather than behind a flag, deliberately: the failure this is for did not reproduce on
        // demand, so the only trace worth having is the one from the run that happened to fail.
        //
        // Snapshots only, and the reason is the whole of #625. Capturing a screenshot on every action puts
        // a small pause and a rendering-pipeline flush in front of each one, which was quietly settling the
        // page — with screenshots on this suite ran 3/3 green and with them off 4/4 red, same machine, same
        // commit, back to back. That is a gate passing for a reason unrelated to the code under test, which
        // is worse than a gate that fails: the page really was unstable (see the font routing below), and
        // the harness was hiding it. Turning them off is what made the failure reproducible enough to fix.
        //
        // Nothing is lost. TestArtifacts already writes a full-page PNG for every test, and what a stuck
        // journey needs is the DOM, which is what a snapshot is. Sources are off too: the C# stack is
        // already in the test output.
        await _ctx.Tracing.StartAsync(new TracingStartOptions
        {
            Screenshots = false,
            Snapshots = true,
            Sources = false
        });

        // Serve the showcase's web fonts from nowhere, so the gate does not depend on a CDN.
        //
        // App.cs links three Google Font families with `display=swap`. Swap means: paint with the fallback
        // now, and REFLOW when each webfont lands — three families, several weights, over the public
        // internet, on a page the journey throttles to Slow-3G. Every arrival moves the text and therefore
        // the bounding box of everything below it, and Playwright's actionability check requires a box that
        // is identical across two consecutive animation frames. That is #625: "element is not stable" for
        // the full 30s, on whichever guide page the walk had reached, on all three hosts (they share this
        // shell), with the text assertions on the very same subtree passing because innerText does not care
        // what font it is in.
        //
        // Aborting the requests makes the page render in the fallback font immediately and settle once.
        // It costs the gate nothing — no assertion is about typography — and it removes a third-party CDN
        // from the definition of "the browser gate is green".
        await _ctx.RouteAsync("**://fonts.googleapis.com/**", route => route.AbortAsync());
        await _ctx.RouteAsync("**://fonts.gstatic.com/**", route => route.AbortAsync());

        Page = await _ctx.NewPageAsync();
        // Capture the browser console + uncaught page errors so a failing test can surface
        // the real client-side cause (e.g. a scoped-JS "Could not find … on target" force-fault
        // that trips RootErrorBoundary) in the CI log — the C# stack alone only shows the wait
        // timeout, not why the app never became interactive.
        Page.Console += (_, msg) =>
        {
            lock (_console)
            {
                _console.Add($"[{msg.Type}] {msg.Text}");
            }
        };
        Page.PageError += (_, err) =>
        {
            lock (_console)
            {
                _console.Add($"[pageerror] {err}");
            }
        };

        // Hook for hosts that must wire the page BEFORE the journey navigates. The browser-served hosts
        // (Server/Wasm) need nothing here — they GET a live HTTP host. The native host runs IN-PROCESS,
        // so NativeExampleTests uses this to install its Playwright-backed INativeWebView (route the shell +
        // client + scoped/static assets, expose the __raskSend bridge) and start NativeAppHost.RunLocalAsync
        // on this exact page, so the client's boot `ready` reaches the in-process session.
        await ConfigurePageAsync();
    }

    public async Task DisposeAsync()
    {
        await TeardownAsync();
        await _ctx.DisposeAsync();
    }

    // Default no-op: the HTTP-served hosts need no per-page wiring. Native overrides it (see above).
    protected virtual Task ConfigurePageAsync() => Task.CompletedTask;

    // Default no-op: paired with ConfigurePageAsync so a host that spun up in-process resources (the
    // native NativeApp) can tear them down before the browser context closes.
    protected virtual Task TeardownAsync() => Task.CompletedTask;

    // Default = direct deep link. Overridden by hosts (e.g. WasmAppHost) that don't install
    // a SPA fallback; those must navigate via the home shell + sidebar instead.
    protected virtual Task NavigateToAsync(string path) => Page.GotoAsync(path);

    // Sidebar groups are collapsed by default, and a collapsed link is display:none — which means
    // Playwright's text engines can't even find it (they match visible text). So navigate the way a
    // user would when the list is long: type the label into the filter, which narrows the sidebar to
    // the matching link at the top. Wait for that link to render (the controlled-input morph) and
    // settle before clicking. The filter is left set (the next nav's FillAsync replaces it) — clearing
    // it here would fire an extra input event that can race with the page interaction that follows the
    // navigation (on WASM the events coalesce and the later one wins, dropping e.g. a select change).
    protected async Task ClickSidebar(string label)
    {
        var filter = Page.Locator(".side-nav .side-nav-filter");
        await filter.FillAsync(label);
        // Guides-first: a label can appear as BOTH an example page and a guide (e.g. "Routing",
        // "Lifecycle"), and the Guides section renders first. The journey's example walks want the
        // example page, so prefer a link that isn't a /guides/* one; guide-only labels (Composition,
        // Forms & validation, Browser APIs, …) have no example link and fall back to the guide.
        var escaped = label.Replace("\"", "\\\"");
        var any = Page.Locator($".side-nav a.side-nav-link:has-text(\"{escaped}\")");
        await any.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await Page.WaitForTimeoutAsync(200);
        var example = Page.Locator($".side-nav a.side-nav-link:has-text(\"{escaped}\"):not([href^=\"/guides/\"])");
        var link = await example.CountAsync() > 0 ? example.First : any.First;
        await link.ClickAsync();
    }

    protected async Task RunAsync(Func<Task> body, [CallerMemberName] string testName = "")
    {
        var traced = false;
        try
        {
            await body();
        }
        catch
        {
            traced = true;
            await SaveTraceAsync(testName);
            await DumpDiagnosticsAsync(testName);
            throw;
        }
        finally
        {
            if (!traced)
            {
                // Stop with no path: the trace is discarded, so a green run leaves nothing behind.
                try { await _ctx.Tracing.StopAsync(); }
                catch
                {
                    /* context may already be gone */
                }
            }

            string[] console;
            lock (_console)
            {
                console = _console.ToArray();
            }

            await TestArtifacts.DumpAsync(Page, FixtureName, testName, ServerLog, console);
        }
    }

    // Writes the journey's trace next to the rest of its artifacts and prints the command that opens it —
    // a trace nobody knows how to look at is not evidence.
    private async Task SaveTraceAsync(string testName)
    {
        try
        {
            var path = Path.Combine(TestArtifacts.DirectoryFor(FixtureName, testName), "trace.zip");
            await _ctx.Tracing.StopAsync(new TracingStopOptions { Path = path });
            Console.WriteLine($"  trace: {path}");
            Console.WriteLine($"    open with: pwsh <playwright.ps1> show-trace {path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  trace: could not be saved ({ex.GetType().Name}: {ex.Message})");
        }
    }

    // On failure, print the browser console + whether the app fell back to the
    // RootErrorBoundary directly into the test process stdout, so it lands in the CI
    // log (which captures stdout) without having to download the test-results artifact.
    private async Task DumpDiagnosticsAsync(string testName)
    {
        string[] console;
        lock (_console)
        {
            console = _console.ToArray();
        }

        string? boundary = null;
        try
        {
            var html = await Page.ContentAsync();
            if (html.Contains("Something went wrong", StringComparison.Ordinal))
            {
                boundary = "RootErrorBoundary fallback PRESENT (\"Something went wrong\") — the app crashed";
            }
        }
        catch
        {
            /* page may be closed */
        }

        Console.WriteLine($"===== E2E DIAG {FixtureName}.{testName} =====");
        Console.WriteLine($"  url: {Page.Url}");
        if (boundary is not null)
        {
            Console.WriteLine($"  {boundary}");
        }

        Console.WriteLine($"  browser console ({console.Length} msgs):");
        foreach (var line in console.TakeLast(40))
        {
            Console.WriteLine($"    {line}");
        }

        Console.WriteLine($"===== /E2E DIAG {FixtureName}.{testName} =====");
    }
}
