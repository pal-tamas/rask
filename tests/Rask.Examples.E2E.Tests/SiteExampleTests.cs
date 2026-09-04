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

            // PHASE ONE — the prerendered document, before the bundle has booted.
            //
            // The headline is in the HTML the server sent. It is asserted with no timeout extension
            // because waiting would defeat the point: if this needs to wait, it was not prerendered.
            await Expect(page.Locator("h1")).ToContainTextAsync("Ship a whole product");
            await Expect(page.Locator("h1")).ToContainTextAsync("C#");

            // PHASE TWO — the bundle takes the page over.
            //
            // This wait is load-bearing, and it is new. It used to be enough to wait for the <h1>,
            // because the <h1> did not exist until the app had rendered it; prerendering made that
            // signal fire immediately and the clicks below started racing the runtime — the first one
            // landed on markup whose handler was not attached yet and was silently lost, so a test that
            // clicked three times saw two.
            //
            // That is a real property of a prerendered page, not a test artifact: it looks interactive
            // before it is. The shell ships <body data-rask-root> with no value and the runtime stamps
            // the session id onto it when it mounts, so this is the framework's own "I have taken over"
            // signal rather than a sleep.
            await Expect(page.Locator("body[data-rask-root='wasm']"))
                .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 60_000 });

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
            const string installer = "curl -sSL https://rask.sh/rask.sh | sh";

            await Expect(page.Locator(".term")).ToContainTextAsync(installer);
            await page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = "WASM" }).ClickAsync();
            await Expect(page.Locator(".term")).ToContainTextAsync("rask new MyApp --template wasm");
            await Expect(page.Locator(".term")).ToContainTextAsync(installer);
            await page.GetByRole(AriaRole.Tab, new PageGetByRoleOptions { Name = "Server" }).ClickAsync();
            await Expect(page.Locator(".term")).ToContainTextAsync("rask new MyApp");
            await Expect(page.Locator(".term")).ToContainTextAsync(installer);

            // Windows can't run a .sh, and rask.sh refuses under MINGW/MSYS and points here.
            await Expect(page.Locator(".install-foot").First)
                .ToContainTextAsync("irm https://rask.sh/rask.ps1 | iex");

            // The hero leads with the headline and the component's own source, where a 500-line generated
            // SVG animation used to be. One <h1>, inside the hero grid.
            await Expect(page.Locator(".hero-grid h1")).ToHaveCountAsync(1);

            // Every feature card is the way into the guide about it. Asserted as a shape rather than by
            // name — the exact set of cards is the page's editorial business — but a page that lost the
            // links entirely, or grew one pointing somewhere other than the docs app, fails here.
            // GuideLinkTests separately asserts each slug resolves to a doc that exists.
            var guideLinks = page.Locator("a.guide-link");
            Assert.True(await guideLinks.CountAsync() >= 20, "the feature cards no longer link into the docs");
            await Expect(guideLinks.First).ToHaveAttributeAsync("target", "_blank");

            foreach (var href in await guideLinks.EvaluateAllAsync<string[]>(
                         "els => els.map(e => e.getAttribute('href'))"))
            {
                Assert.StartsWith("docs/guides/", href, StringComparison.Ordinal);
            }

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
