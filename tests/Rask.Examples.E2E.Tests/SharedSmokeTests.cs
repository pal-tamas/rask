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
    }

    public async Task DisposeAsync() => await _ctx.DisposeAsync();

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
        var link = Page.Locator(".side-nav a.side-nav-link:has-text(\"" + label + "\")").First;
        await link.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await Page.WaitForTimeoutAsync(200);
        await link.ClickAsync();
    }

    protected async Task RunAsync(Func<Task> body, [CallerMemberName] string testName = "")
    {
        try
        {
            await body();
        }
        catch
        {
            await DumpDiagnosticsAsync(testName);
            throw;
        }
        finally
        {
            string[] console;
            lock (_console)
            {
                console = _console.ToArray();
            }

            await TestArtifacts.DumpAsync(Page, FixtureName, testName, ServerLog, console);
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
