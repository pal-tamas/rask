using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     The published site follows the reader's operating system until they choose a palette, and lets them
///     choose again — including back.
/// </summary>
/// <remarks>
///     <para>
///     None of this is reachable from a unit test. <c>ThemeContrastTests</c> proves the PALETTES are
///     readable by computing every pair out of the shipped stylesheets, which is the half that arithmetic
///     can settle. Whether a palette is the one actually APPLIED is a question about the cascade, a media
///     query and <c>localStorage</c> — three things only a browser has.
///     </para>
///     <para>
///     The mechanism under test is an absence, which is why it needs guarding. With no stored choice the
///     boot script writes NO <c>data-theme</c> at all, because daisyUI compiles the default palette as
///     <c>:where([data-rask-ui])</c> plus <c>[data-rask-ui]:not([data-theme])</c> inside
///     <c>@media (prefers-color-scheme: dark)</c> — so the absent attribute IS the feature. Anything that
///     "helpfully" stamps a default would pass every markup assertion in the suite and silently pin every
///     reader to light, which is what this site did until now.
///     </para>
/// </remarks>
[Collection(WasmExampleCollection.Name)]
public sealed class ThemeTests
{
    private readonly WasmExampleAppFixture _app;
    private readonly PlaywrightFixture _pw;

    public ThemeTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    {
        _app = app;
        _pw = pw;
    }

    [Theory]
    [InlineData(ColorScheme.Light)]
    [InlineData(ColorScheme.Dark)]
    public async Task WithNoStoredChoice_TheOperatingSystemDecides(ColorScheme scheme)
    {
        var context = await _pw.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = _app.BaseUrl,
            ColorScheme = scheme,
        });

        var page = await context.NewPageAsync();

        try
        {
            await page.GotoAsync("/index.html");
            await Expect(page.Locator("h1")).ToContainTextAsync("Ship a whole product");

            // No attribute, in either direction. The palette is decided by the media query alone.
            Assert.Null(await page.Locator("html").GetAttributeAsync("data-theme"));

            // `color-scheme` comes from the theme block daisyUI matched, so it reports which one won —
            // and it also drives the form controls and scrollbars the page does not style itself.
            var colorScheme = await page.EvaluateAsync<string>(
                "() => getComputedStyle(document.documentElement).colorScheme");

            Assert.Equal(scheme == ColorScheme.Dark ? "dark" : "light", colorScheme);

            // And the ground actually painted that way. A dark palette whose background stayed white is
            // the failure this is really about: it looks like a theme bug and reads as unstyled text.
            var luminance = await Luminance(page, "document.documentElement");

            if (scheme == ColorScheme.Dark)
            {
                Assert.True(luminance < 0.2, $"the dark palette painted a light ground (luminance {luminance:0.000}).");
            }
            else
            {
                Assert.True(luminance > 0.7, $"the light palette painted a dark ground (luminance {luminance:0.000}).");
            }

            // Body text clears AA against whichever ground won. Measured rather than assumed: this is the
            // pair that was 1.26:1 on a dark machine when muted text aliased daisyUI's neutral SURFACE.
            foreach (var token in new[] { "--color-ui-ink", "--color-ui-muted" })
            {
                var ratio = await Contrast(page, token);
                Assert.True(ratio >= 4.5, $"{token} measures {ratio:0.00}:1 on the {scheme} ground.");
            }
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task AChosenPaletteIsAppliedRememberedAndReversible()
    {
        var context = await _pw.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = _app.BaseUrl,
            ColorScheme = ColorScheme.Light,
        });

        var page = await context.NewPageAsync();

        try
        {
            await page.GotoAsync("/index.html");
            await Expect(page.Locator("h1")).ToContainTextAsync("Ship a whole product");

            // Through the same entry point the picker's radios use, so the test exercises the path a
            // reader does rather than a private helper.
            await page.EvaluateAsync("() => window.raskSetTheme('dracula')");
            await Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", "dracula");

            // Dracula is a dark palette, so choosing it must beat the light machine this context claims.
            Assert.True(await Luminance(page, "document.documentElement") < 0.2, "the chosen dark palette did not paint.");

            // REMEMBERED. Nothing in CSS can persist a choice, and before the boot script existed the
            // palette reset on every navigation and on the WASM first frame.
            await page.ReloadAsync();
            await Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", "dracula");

            // REVERSIBLE. "system" is not a palette daisyUI compiled — selecting it must REMOVE the
            // attribute, not stamp it. Stamping would match no block and leave every --color-base-*
            // undefined: a fully laid-out page with no colour in it.
            await page.EvaluateAsync("() => window.raskSetTheme('system')");
            Assert.Null(await page.Locator("html").GetAttributeAsync("data-theme"));
            Assert.True(await Luminance(page, "document.documentElement") > 0.7, "going back to the OS did not restore the light ground.");

            await page.ReloadAsync();
            Assert.Null(await page.Locator("html").GetAttributeAsync("data-theme"));

            // A value that is not a theme is not a choice. localStorage is reader-writable and data-theme
            // is matched by value, so an unvalidated one would be stamped verbatim and uncolour the page.
            await page.EvaluateAsync("() => localStorage.setItem('rask-theme', 'dracola')");
            await page.ReloadAsync();
            Assert.Null(await page.Locator("html").GetAttributeAsync("data-theme"));
            Assert.True(await Luminance(page, "document.documentElement") > 0.7, "an unknown stored theme was applied.");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task ThePickerOffersTheWayBackToTheOperatingSystem()
    {
        var context = await _pw.Browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = _app.BaseUrl });
        var page = await context.NewPageAsync();

        try
        {
            await page.GotoAsync("/index.html");
            await Expect(page.Locator("h1")).ToContainTextAsync("Ship a whole product");

            // It is in the prerendered document, not added after boot: a reader on a dark machine who wants
            // light should not have to wait for a WASM runtime to download before they can say so.
            await Expect(page.Locator("input.theme-controller[value='system']").First).ToHaveCountAsync(1);

            // And it is the checked one while nothing is stored, so the control tells the truth about what
            // is showing. The CSS-only picker renders every radio unchecked; the boot script re-marks it.
            await Expect(page.Locator("input.theme-controller[value='system']").First).ToBeCheckedAsync();
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    /// <summary>The relative luminance of an element's painted background.</summary>
    private static Task<double> Luminance(IPage page, string element) =>
        page.EvaluateAsync<double>(Rgb + $"(() => lum(getComputedStyle({element}).backgroundColor))()");

    /// <summary>A token's contrast against the document's own ground, as a browser composites it.</summary>
    private static Task<double> Contrast(IPage page, string token) =>
        page.EvaluateAsync<double>(Rgb + $$"""
            (() => {
              const probe = document.createElement('span');
              probe.style.cssText = 'position:fixed;opacity:0';
              probe.style.color = 'var({{token}})';
              document.body.appendChild(probe);
              const fg = lum(getComputedStyle(probe).color);
              probe.remove();
              const bg = lum(getComputedStyle(document.documentElement).backgroundColor);
              return (Math.max(fg, bg) + 0.05) / (Math.min(fg, bg) + 0.05);
            })()
            """);

    /// <summary>
    ///     Luminance of any CSS colour, measured through a canvas.
    /// </summary>
    /// <remarks>
    ///     The conversion is the point. Chromium reports a computed colour in the space it was AUTHORED in
    ///     — <c>oklch(0.2533 0.016 252.42)</c> for a daisyUI token, <c>oklab(…)</c> for a
    ///     <c>color-mix()</c> result — so anything that parses the string as <c>rgb()</c> silently scores
    ///     NaN and every assertion built on it passes. A 1x1 canvas converts it the way the compositor
    ///     does, which is also the only form a WCAG ratio may be computed from.
    /// </remarks>
    private const string Rgb = """
        const lum = (value) => {
          const c = document.createElement('canvas');
          c.width = c.height = 1;
          const ctx = c.getContext('2d', { willReadFrequently: true });
          ctx.fillStyle = '#000';
          ctx.fillStyle = value;
          ctx.fillRect(0, 0, 1, 1);
          const d = ctx.getImageData(0, 0, 1, 1).data;
          const ch = (v) => { v /= 255; return v <= 0.04045 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); };
          return 0.2126 * ch(d[0]) + 0.7152 * ch(d[1]) + 0.0722 * ch(d[2]);
        };
        """;
}
