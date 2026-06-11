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
        _ctx = await _pw.Browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = BaseUrl });
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

    protected Task ClickSidebar(string label) =>
        Page.Locator("aside.side-nav button.nav-item-btn:has-text(\"" + label + "\")").ClickAsync();

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
