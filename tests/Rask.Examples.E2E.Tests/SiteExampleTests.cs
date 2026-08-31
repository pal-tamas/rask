using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

/// <summary>
///     The marketing landing site (<c>Rask.Example.Site</c>), published and served from a plain static
///     host (<see cref="SiteWasmAppFixture" />) — the GitHub Pages front door. The whole page is rendered
///     by a Rask WASM app, so the journey proves the framework renders a full document shell, that the
///     live counter and install tabs are genuine stateful Rask components (click → diff → re-render), and
///     that the docs/playground links point at the nested sub-apps.
/// </summary>
[Collection(SiteExampleCollection.Name)]
public sealed class SiteExampleTests
{
    private readonly SiteWasmAppFixture _app;
    private readonly PlaywrightFixture _pw;

    public SiteExampleTests(SiteWasmAppFixture app, PlaywrightFixture pw)
    {
        _app = app;
        _pw = pw;
    }

    [Fact]
    public async Task Journey_RendersInRaskWithLiveCounterAndTabs()
    {
        var context = await _pw.Browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = _app.BaseUrl });
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync("/index.html");

            // The hero is rendered by the WASM app and morphs onto the shell — wait for it to boot.
            await Expect(page.Locator("h1")).ToContainTextAsync("C#",
                new LocatorAssertionsToContainTextOptions { Timeout = 60_000 });
            await Expect(page.Locator("h1")).ToContainTextAsync("Ship a whole product");

            // The live counter is a real stateful Rask component: each click ships a diff and re-renders.
            var count = page.Locator(".count");
            await Expect(count).ToHaveTextAsync("0");
            var button = page.Locator("button.count-btn");
            await button.ClickAsync();
            await button.ClickAsync();
            await button.ClickAsync();
            await Expect(count).ToHaveTextAsync("3");

            // The install tabs are Rask state too — switching re-renders the selected terminal.
            // Both terminals must lead with the one-line installer: the tabs pick a TEMPLATE, not an
            // install method, so whichever one a visitor lands on has to show a command that works on a
            // machine with no .NET SDK. This is the published front door — the page is served from
            // GitHub Pages next to the very script it tells you to curl.
            const string installer = "curl -sSL https://pal-tamas.github.io/rask/rask.sh | sh";

            await Expect(page.Locator(".term")).ToContainTextAsync(installer);
            await page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = "WASM" }).ClickAsync();
            await Expect(page.Locator(".term")).ToContainTextAsync("rask new MyApp --template wasm");
            await Expect(page.Locator(".term")).ToContainTextAsync(installer);
            await page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = "Server" }).ClickAsync();
            await Expect(page.Locator(".term")).ToContainTextAsync("rask new MyApp");
            await Expect(page.Locator(".term")).ToContainTextAsync(installer);

            // Windows can't run a .sh, and rask.sh refuses under MINGW/MSYS and points here.
            await Expect(page.Locator(".install-foot").First)
                .ToContainTextAsync("irm https://pal-tamas.github.io/rask/rask.ps1 | iex");

            // The page opens on the chain animation, before it says anything about the framework. The
            // adjacent-sibling selector is the assertion: it only matches if the animation's box comes
            // immediately before the grid that holds the headline, so a future edit cannot quietly demote
            // it below the fold. Rendered inline (not through <img>) so it inherits the theme tokens —
            // which is also why the <svg> element itself is reachable from the DOM at all.
            await Expect(page.Locator(".hero-anim svg")).ToHaveCountAsync(1);
            await Expect(page.Locator(".hero-anim + .hero-grid h1")).ToHaveCountAsync(1);

            // The front door links into the nested docs + playground sub-apps, and names them for what
            // they are — the site is three apps, and calling /docs/ "the live demo" left the docs unnamed.
            await Expect(page.Locator("#cta-docs")).ToHaveAttributeAsync("href", "docs/");
            await Expect(page.Locator("#cta-docs")).ToHaveTextAsync("Docs");

            // The nav "Docs" entry points at the on-site showcase (/docs/), and the old external
            // GitHub-docs link is gone — no nav link targets the repo's markdown folder anymore.
            await Expect(page.Locator("nav a", new PageLocatorOptions { HasTextString = "Docs" }).First)
                .ToHaveAttributeAsync("href", "docs/");
            await Expect(page.Locator("a[href*='tree/main/docs']")).ToHaveCountAsync(0);
        }
        finally
        {
            await context.CloseAsync();
        }
    }
}
