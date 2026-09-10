using System.Globalization;
using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     The marketing landing page at the site root, published and served from a plain static
///     host (<see cref="WasmExampleAppFixture" />) — the GitHub Pages front door. The whole page is rendered
///     by a Rask WASM app, so the journey proves the framework renders a full document shell, that the
///     live counter and install tabs are genuine stateful Rask components (click → diff → re-render), and
///     that the docs link points at the nested sub-app.
/// </summary>
[Collection(WasmExampleCollection.Name)]
public sealed class SiteExampleTests
{
    private readonly WasmExampleAppFixture _app;
    private readonly PlaywrightFixture _pw;

    public SiteExampleTests(WasmExampleAppFixture app, PlaywrightFixture pw)
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

            // The four front-end lanes are part of the prerendered document rather than something the
            // bundle fills in later. This is the section a visitor reads to work out which lane they
            // are in — and the one a crawler has to see all of, since choosing a front end is the
            // decision that brings people to the page at all. Asserted with no timeout extension, for
            // the same reason as the headline above: waiting would mean it was not prerendered.
            await Expect(page.Locator("#front-ends a")).ToHaveCountAsync(4);
            await Expect(page.Locator("#front-ends")).ToContainTextAsync("Meta framework");

            // Note there is deliberately no "the marker is present" assertion here. It is true only
            // until the runtime takes over, and the runtime may well have taken over by the time this
            // line runs — asserting it in a live journey is a race that would pass on a slow machine and
            // fail on a fast one. That the publish writes it is pinned where it is deterministic, in
            // PrerenderShellTests; what this journey adds is the half no unit test can see. (#973)

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

            // …and the prerendered marker is gone, because the page IS interactive now. The pair is the
            // contract: an app styling `[data-rask-prerendered]` gets that styling removed at exactly the
            // moment its controls start working. Asserting only that the attribute appears would pass
            // just as well if nothing ever cleared it, which is the state this fixed. (#973)
            await Expect(page.Locator("html[data-rask-prerendered]")).ToHaveCountAsync(0);

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

            // The prompt has a gap after it (#1032). Measured on the rendered page, because that is the
            // only place this is decided and because the obvious reading of the stylesheet is wrong: a
            // sheet that contains a correcting rule and a page that shows the gap are different claims.
            //
            // The gap is made of WIDTH, not margin. daisyUI right-aligns the prompt inside a fixed 2rem
            // box, and the kit widens that box; a margin cannot work here at all, because a consuming
            // app's Tailwind preflight resets margin on ::before from its own <link> and layers do not
            // merge across sheets. So this asserts the box is wider than the 2rem daisyUI sets — which
            // is exactly the difference between the prompt sitting on the command and clear of it.
            var prompt = await page.Locator(".term pre[data-prefix]").First.EvaluateAsync<string>(
                @"el => {
                    const s = getComputedStyle(el, '::before');
                    return JSON.stringify({ width: s.width, content: s.content, textAlign: s.textAlign });
                }");

            var width = System.Text.Json.JsonDocument.Parse(prompt).RootElement
                .GetProperty("width").GetString() ?? "";

            Assert.True(
                double.TryParse(width.Replace("px", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var widthPx)
                && widthPx > 32,
                $"the install command is flush against its prompt: computed ::before was {prompt}");

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

            // They navigate as an SPA, and the two halves of that are asserted separately because
            // either one alone is a link that reloads the app.
            //
            // NO `target`: a target of any kind is what the runtime's link interception declines to
            // touch, and these cards all carried target="_blank" — so every card on the front door
            // opened a SECOND TAB and cold-booted the whole WASM bundle, boot screen and all.
            //
            // WITH `data-rask-nav`: that attribute IS the interception's selector, and only NavLink
            // writes it. A bare <a href> to an in-app route is a full document navigation no matter
            // how internal the URL is, which is the other half of the same defect. (#1058)
            await Expect(guideLinks.First).Not.ToHaveAttributeAsync("target", "_blank");
            Assert.Equal(await guideLinks.CountAsync(), await page.Locator("a.guide-link[data-rask-nav]").CountAsync());

            foreach (var href in await guideLinks.EvaluateAllAsync<string[]>(
                         "els => els.map(e => e.getAttribute('href'))"))
            {
                Assert.StartsWith("/docs/guides/", href, StringComparison.Ordinal);
            }

            // The front door links to the showcase and names it for what it is — calling /docs "the
            // live demo" left the docs themselves unnamed. An in-app route now (one app, two areas),
            // where it used to be a relative link from one published app to another.
            await Expect(page.Locator("#cta-docs")).ToHaveAttributeAsync("href", SharedSmokeTests.Docs);
            await Expect(page.Locator("#cta-docs")).ToHaveTextAsync("Docs");

            // The nav "Docs" entry points at the on-site showcase (/docs/), and the old external
            // GitHub-docs link is gone — no nav link targets the repo's markdown folder anymore.
            await Expect(page.Locator("nav a", new PageLocatorOptions { HasTextString = "Docs" }).First)
                .ToHaveAttributeAsync("href", SharedSmokeTests.Docs);
            await Expect(page.Locator("a[href*='tree/main/docs']")).ToHaveCountAsync(0);
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    /// <summary>
    ///     Taking the page over must not move anything on it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is the flicker (#1058), pinned as a measurement rather than a description. The page's
    ///         three faces used to arrive from a font CDN through the standard non-blocking pattern —
    ///         <c>&lt;link media="print" onload="this.media='all'"&gt;</c> — and that pattern is quietly
    ///         incompatible with a full-document morph: the link is a KEYED head asset, so the first WASM
    ///         frame reconciled <c>media</c> back to the rendered "print", un-applied the faces, reflowed
    ///         the document to fallback metrics, and reflowed back when the onload re-fired. Measured on
    ///         rask.sh: 5757px → 5705px → 5757px of document height, the <c>&lt;h1&gt;</c> line box
    ///         56px → 61px. It repeated on every cross-route navigation, because every one of those is
    ///         another full morph.
    ///     </para>
    ///     <para>
    ///         Height, and the <c>&lt;h1&gt;</c>'s own box, before and after the runtime takes over. Both
    ///         are text metrics: if a face swaps at hydration they move, and any tolerance wide enough to
    ///         pass that is wide enough to be worthless. A page whose own content changed at hydration
    ///         would fail here too, which is correct — that is also a flicker.
    ///     </para>
    /// </remarks>
    [Theory]
    // The front door, and the docs — the reflow was reported on both, and it would be: every
    // prerendered page is taken over by the same morph, so any page-shaped assumption here is one
    // page's luck. The docs page is also the one a reader lands on most often from outside.
    [InlineData("/index.html", "Ship a whole product")]
    [InlineData("/docs/index.html", "Guides")]
    public async Task Hydration_DoesNotReflowThePage(string path, string headline)
    {
        var context = await _pw.Browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = _app.BaseUrl });
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync(path);

            // The prerendered document, before the bundle boots. Read after the fonts have settled so
            // this is the steady state being compared, not a frame mid-load.
            await Expect(page.Locator("h1")).ToContainTextAsync(headline);
            await page.EvaluateAsync("() => document.fonts.ready");
            var before = await Measure(page);

            await Expect(page.Locator("body[data-rask-root='wasm']"))
                .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 60_000 });
            await Expect(page.Locator("html[data-rask-prerendered]")).ToHaveCountAsync(0);
            await page.WaitForTimeoutAsync(1_000);
            var after = await Measure(page);

            Assert.Equal(before, after);
        }
        finally
        {
            await context.CloseAsync();
        }

        static async Task<string> Measure(IPage page) => await page.EvaluateAsync<string>(
            @"() => {
                const h1 = document.querySelector('h1').getBoundingClientRect();
                return [
                    'doc=' + Math.round(document.body.getBoundingClientRect().height),
                    'h1=' + Math.round(h1.height) + 'x' + Math.round(h1.width),
                    'face=' + getComputedStyle(document.querySelector('h1')).fontFamily.split(',')[0]
                ].join(' ');
            }");
    }

    /// <summary>
    ///     The faces are served from this origin, and nothing defers a stylesheet to get them.
    /// </summary>
    /// <remarks>
    ///     The companion to <see cref="Hydration_DoesNotReflowThePage" />: that one measures the symptom,
    ///     this one pins the cause out of existence. A reintroduced CDN <c>&lt;link&gt;</c> — or any
    ///     <c>media="print"</c> stylesheet flipped by an onload — brings the reflow back with it.
    /// </remarks>
    [Fact]
    public async Task Fonts_AreSelfHostedAndPreloaded()
    {
        var context = await _pw.Browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = _app.BaseUrl });
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync("/index.html");

            await Expect(page.Locator("head link[href*='fonts.googleapis.com']")).ToHaveCountAsync(0);
            await Expect(page.Locator("head link[href*='fonts.gstatic.com']")).ToHaveCountAsync(0);
            await Expect(page.Locator("head link[rel='stylesheet'][media='print']")).ToHaveCountAsync(0);

            // Preloaded, and `crossorigin` with them: a font is fetched in CORS mode even same-origin,
            // so a preload without it is a second unshared request and the preload buys nothing.
            await Expect(page.Locator("head link[rel='preload'][as='font'][crossorigin]")).ToHaveCountAsync(3);

            await page.EvaluateAsync("() => document.fonts.ready");
            var loaded = await page.EvaluateAsync<string[]>(
                "() => Array.from(document.fonts).filter(f => f.status === 'loaded').map(f => f.family)");
            Assert.Contains("Inter", loaded);
            Assert.Contains("Space Grotesk", loaded);
            Assert.Contains("JetBrains Mono", loaded);
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    /// <summary>
    ///     A theme the reader picks is still there after a navigation and after a reload, and light is
    ///     what they get before they have picked anything.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The picker used to be daisyUI's CSS-only <c>theme-controller</c>, which cannot persist
    ///         anything: no script to write a choice with, and a radio that renders unchecked on every
    ///         pass, so the next render — a navigation, or the WASM first frame — put the theme back to
    ///         the default. ThemeMenu owns the value and hands it to the boot script, which writes
    ///         <c>localStorage</c> and stamps <c>&lt;html&gt;</c>.
    ///     </para>
    ///     <para>
    ///         The pre-hydration read is the load-bearing one. The script runs before the first paint, so
    ///         a saved theme is on the document from the first frame — not corrected afterwards, which
    ///         would be a flash of the wrong palette and the very thing this pair of fixes is about.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task Theme_FollowsTheOperatingSystemAndIsRemembered()
    {
        var context = await _pw.Browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = _app.BaseUrl });
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync("/index.html");
            await Expect(page.Locator("body[data-rask-root='wasm']"))
                .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 60_000 });

            // NO data-theme with nothing stored, and the absence is the feature: daisyUI compiles
            // [data-rask-ui]:not([data-theme]) under prefers-color-scheme, so the attribute's absence is
            // what follows the reader's machine. This used to assert "light", which is what pinned every
            // reader to a white page however their OS was set. ThemeTests measures both directions and the
            // contrast in each; this journey only has to prove the picker still round-trips.
            Assert.Null(await page.Locator("html").GetAttributeAsync("data-theme"));
            Assert.Null(await page.EvaluateAsync<string?>("() => localStorage.getItem('rask-theme')"));

            await page.Locator("details.dropdown > summary").First.ClickAsync();
            await page.Locator("input.theme-controller[value='dracula']").First.CheckAsync();

            await Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", "dracula");
            Assert.Equal("dracula", await page.EvaluateAsync<string?>("() => localStorage.getItem('rask-theme')"));

            // Survives a reload — and is already right BEFORE the runtime is back, because the boot
            // script applies it in <head> rather than a component applying it after the first frame.
            await page.ReloadAsync();
            await Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", "dracula");
            await Expect(page.Locator("body[data-rask-root='wasm']"))
                .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 60_000 });
            await Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", "dracula");

            // …and the picker shows WHICH one, which is the half the CSS-only control cannot do for
            // itself: it renders every radio unchecked, so without the script's mark() a reader coming
            // back to a dracula page would find the list claiming nothing was chosen.
            await Expect(page.Locator("input.theme-controller[value='dracula']")).ToBeCheckedAsync();

            // And the picker adds NO handlers to the page — the property the islands depend on. Any
            // C# handler in this chrome shifts every handler id after it, and an island holds the id it
            // read from the prerendered markup. (h28 -> h63 with 35 buttons; h28 -> h29 with one.)
            Assert.Equal(0, await page.Locator("details.dropdown [data-rask-on-click]").CountAsync());
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    /// <summary>
    ///     Clicking "Docs" navigates in this tab, in this document — no reload, no second tab.
    /// </summary>
    /// <remarks>
    ///     It opened a new tab. NavItem stamped <c>target="_blank"</c> on every entry including the
    ///     internal one, so the front door's own Docs link cold-booted the entire WASM app again. The
    ///     proof that it is now an SPA commit is a value set on <c>window</c> before the click surviving
    ///     it: a document navigation of any kind takes that with it. (#1058)
    /// </remarks>
    [Fact]
    public async Task DocsLink_NavigatesInPlaceWithoutReloadingTheApp()
    {
        var context = await _pw.Browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = _app.BaseUrl });
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync("/index.html");
            await Expect(page.Locator("body[data-rask-root='wasm']"))
                .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 60_000 });

            await page.EvaluateAsync("() => { window.__spaWitness = 'alive'; }");
            var tabsBefore = context.Pages.Count;

            await page.Locator("header a[data-rask-nav]", new PageLocatorOptions { HasTextString = "Docs" })
                .First.ClickAsync();

            await Expect(page.Locator("h1")).ToContainTextAsync("Guides");
            Assert.EndsWith("/docs", page.Url, StringComparison.Ordinal);
            Assert.Equal(tabsBefore, context.Pages.Count);
            Assert.Equal("alive", await page.EvaluateAsync<string?>("() => window.__spaWitness"));
        }
        finally
        {
            await context.CloseAsync();
        }
    }
}
