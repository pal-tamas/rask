using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

/// <summary>
///     The live-playground sub-app (<c>Rask.Example.Playground</c>), published and served from a plain
///     static host (<see cref="PlaygroundAppFixture" />) — the GitHub Pages scenario. One comprehensive
///     journey proves the browser-only story that unit tests can't reach: the app boots, compiles the
///     starter component <em>in the browser</em> with Roslyn + the Rask source generator, renders the
///     result, and the compiled component's own click handler runs through the shared live session.
/// </summary>
[Collection(PlaygroundExampleCollection.Name)]
public sealed class PlaygroundExampleTests
{
    private readonly PlaygroundAppFixture _app;
    private readonly PlaywrightFixture _pw;

    public PlaygroundExampleTests(PlaygroundAppFixture app, PlaywrightFixture pw)
    {
        _app = app;
        _pw = pw;
    }

    [Fact]
    public async Task Compiles_and_runs_user_code_live_in_the_browser()
    {
        var page = await _pw.Browser.NewPageAsync(new BrowserNewPageOptions { BaseURL = _app.BaseUrl });
        try
        {
            await page.GotoAsync("/index.html");

            // Boot: the playground's own UI first render (WASM runtime + WasmLiveSession).
            await Expect(page.Locator(".pg-run"))
                .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 90_000 });

            // Compile + run the default starter. First compile downloads the framework assemblies as Roslyn
            // references and compiles interpreted, so allow generous headroom.
            await page.ClickAsync(".pg-run");
            await Expect(page.Locator(".pg-preview .card"))
                .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 180_000 });
            await Expect(page.Locator(".pg-preview .card p")).ToContainTextAsync("0 times");

            // Interactivity: the compiled component's click handler dispatches through the playground's
            // single live session and re-renders the preview subtree in place.
            await page.ClickAsync(".pg-preview .card .btn");
            await Expect(page.Locator(".pg-preview .card p"))
                .ToContainTextAsync("1 times", new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });

            // Regression guard: Monaco injects its theme colours as a <style> in <head>; the live-diff
            // morph on each re-render (Run + this click) must preserve it (it's marked data-rask-managed),
            // or the editor loses all syntax colouring and renders faint. Assert it survived.
            var monacoHeadStyles = await page.EvaluateAsync<int>(
                "() => document.querySelectorAll('head style.monaco-colors, head style[class*=\"monaco\"]').length");
            Assert.True(monacoHeadStyles > 0,
                "Monaco's head-injected theme <style> was stripped on re-render — the editor would render uncoloured.");
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
