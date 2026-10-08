using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     Dark mode on the published site, the way Flux does it: a <c>dark</c> class on <c>&lt;html&gt;</c>,
///     flipped by the moon in the top bar, remembered, and following the operating system until then.
/// </summary>
/// <remarks>
///     None of this is reachable from a unit test: whether the page IS dark is a question about the
///     cascade, a media query and <c>localStorage</c>, three things only a browser has.
/// </remarks>
[Collection(WasmExampleCollection.Name)]
public sealed partial class DarkModeTests
{
    private const string Moon = "header button[data-appearance-toggle]";

    private readonly WasmExampleAppFixture _app;
    private readonly PlaywrightFixture _pw;

    public DarkModeTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    {
        _app = app;
        _pw = pw;
    }

    [Theory]
    [InlineData(ColorScheme.Light)]
    [InlineData(ColorScheme.Dark)]
    public async Task With_nothing_stored_the_operating_system_decides(ColorScheme scheme)
    {
        var context = await NewContextAsync(scheme);
        var page = await context.NewPageAsync();

        await page.GotoAsync("/index.html");
        await Expect(page.Locator("h1")).ToContainTextAsync("The whole stack");

        try
        {
            var dark = scheme == ColorScheme.Dark;
            Assert.Equal(dark, await IsDarkAsync(page));
            Assert.Null(await StoredAsync(page));
            // daisyUI still draws most of the page and reads data-theme; this line goes with it.
            await Expect(page.Locator("html")).ToHaveAttributeAsync("data-theme", dark ? "dark" : "light");
            await AssertGroundAsync(page, dark);

            // Body text clears AA against whichever ground won.
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
    public async Task The_moon_flips_the_page_and_the_choice_survives_a_reload_and_a_navigation()
    {
        var context = await NewContextAsync(ColorScheme.Light);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/index.html");
        await Expect(page.Locator("h1")).ToContainTextAsync("The whole stack");

        await page.Locator(Moon).ClickAsync();

        try
        {
            await Expect(page.Locator("html")).ToHaveClassAsync(DarkClass());
            Assert.Equal("dark", await StoredAsync(page));
            await AssertGroundAsync(page, dark: true);

            // Remembered, and already dark BEFORE the runtime is back: the script runs in <head>.
            await page.ReloadAsync();
            Assert.True(await IsDarkAsync(page), "the reloaded page did not start dark.");
            await Expect(page.Locator("body[data-rask-root='wasm']"))
                .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 60_000 });
            Assert.True(await IsDarkAsync(page), "hydration dropped the dark class.");

            // A navigation morphs <html>, which strips what the app did not render.
            await page.Locator("header a[data-rask-nav]", new PageLocatorOptions { HasTextString = "Docs" })
                .First.ClickAsync();
            await Expect(page.Locator("h1")).ToContainTextAsync("Guides");
            Assert.True(await IsDarkAsync(page), "the navigation dropped the dark class.");

            // A choice beats the machine, and the moon flips back.
            await page.EmulateMediaAsync(new PageEmulateMediaOptions { ColorScheme = ColorScheme.Dark });
            await page.Locator(Moon).ClickAsync();
            await Expect(page.Locator("html")).Not.ToHaveClassAsync(DarkClass());
            Assert.Equal("light", await StoredAsync(page));
            await AssertGroundAsync(page, dark: false);
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task System_follows_the_operating_system_while_the_page_is_open()
    {
        var context = await NewContextAsync(ColorScheme.Light);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/index.html");
        await Expect(page.Locator("h1")).ToContainTextAsync("The whole stack");

        await page.EvaluateAsync("() => { Rask.appearance = 'dark'; Rask.appearance = 'system'; }");

        try
        {
            // System is the absence of a key, as Flux stores it.
            Assert.Null(await StoredAsync(page));
            Assert.False(await IsDarkAsync(page));

            await page.EmulateMediaAsync(new PageEmulateMediaOptions { ColorScheme = ColorScheme.Dark });
            await Expect(page.Locator("html")).ToHaveClassAsync(DarkClass());
            await page.EmulateMediaAsync(new PageEmulateMediaOptions { ColorScheme = ColorScheme.Light });
            await Expect(page.Locator("html")).Not.ToHaveClassAsync(DarkClass());

            // Only the exact strings are a choice: localStorage is reader-writable.
            await page.EvaluateAsync("() => localStorage.setItem('rask.appearance', 'dracula')");
            await page.ReloadAsync();
            Assert.False(await IsDarkAsync(page), "an unknown stored value was applied.");
            Assert.Equal("system", await page.EvaluateAsync<string>("() => Rask.appearance"));
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task The_d_key_flips_it_too()
    {
        var context = await NewContextAsync(ColorScheme.Light);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/index.html");
        await Expect(page.Locator("h1")).ToContainTextAsync("The whole stack");

        await page.Keyboard.PressAsync("d");

        try
        {
            await Expect(page.Locator("html")).ToHaveClassAsync(DarkClass());
            await page.Keyboard.PressAsync("Control+d");
            await Expect(page.Locator("html")).ToHaveClassAsync(DarkClass());
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Theory]
    [InlineData("/index.html")]
    [InlineData("/docs/")]
    public async Task The_bar_carries_one_moon_and_no_theme_picker(string path)
    {
        var context = await NewContextAsync(ColorScheme.Light);
        var page = await context.NewPageAsync();

        await page.GotoAsync(path);

        try
        {
            // In the prerendered document: a reader on a dark machine who wants light should not have to
            // wait for a WASM runtime before they can say so.
            var moon = page.Locator(Moon);
            await Expect(moon).ToHaveCountAsync(1);
            // Its tooltip is its name, shortcut and all, as on Flux's own header.
            await Expect(moon).ToHaveAccessibleNameAsync("Toggle dark mode D");
            Assert.Null(await moon.GetAttributeAsync("data-rask-on-click"));
            var box = await moon.BoundingBoxAsync();
            Assert.Equal((40, 40), ((int)box!.Width, (int)box.Height));

            await Expect(page.Locator("input.theme-controller")).ToHaveCountAsync(0);
            await Expect(page.Locator("header button", new PageLocatorOptions { HasTextString = "Theme" }))
                .ToHaveCountAsync(0);
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task In_dark_mode_a_kit_card_is_painted_by_its_dark_variant_not_by_the_sites_own_utility()
    {
        // The bug one stylesheet ends. The kit's card writes `bg-white dark:bg-white/10`; the site writes
        // `bg-white` on elements of its own. With the kit's precompiled sheet linked ahead of the site's,
        // both rules sat in an `@layer utilities` of their own with equal specificity, link order decided,
        // and every card on this page computed rgb(255, 255, 255) in dark mode.
        var context = await NewContextAsync(ColorScheme.Dark);
        var page = await context.NewPageAsync();

        // Reached the way a reader reaches it: the guides index, then the sidebar.
        await page.GotoAsync("/docs/");
        await Expect(page.Locator("body[data-rask-root='wasm']"))
            .ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 60_000 });
        await page.Locator(".side-nav .side-nav-search input").First.FillAsync("Data display");
        await page.Locator(".side-nav a.side-nav-link[href*='/docs/ui/data-display']").First.ClickAsync();

        try
        {
            await Expect(page.Locator("main h1")).ToContainTextAsync("Data display");
            Assert.True(await IsDarkAsync(page));

            // ONE sheet carries Tailwind, the kit and the site's classes.
            await Expect(page.Locator("head link[rel='stylesheet'][href*='/css/app.css']")).ToHaveCountAsync(1);
            await Expect(page.Locator("head link[rel='stylesheet'][href*='rask-ui.css']")).ToHaveCountAsync(0);

            var card = page.Locator("[data-ui-card][class~='bg-white'][class~='dark:bg-white/10']").First;
            await Expect(card).ToBeVisibleAsync();
            var painted = await card.EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor");
            Assert.NotEqual("rgb(255, 255, 255)", painted);
            Assert.Contains("0.1", painted, StringComparison.Ordinal);
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    private Task<IBrowserContext> NewContextAsync(ColorScheme scheme) =>
        _pw.Browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = _app.BaseUrl, ColorScheme = scheme });

    private static Task<bool> IsDarkAsync(IPage page) =>
        page.EvaluateAsync<bool>("() => document.documentElement.classList.contains('dark')");

    private static Task<string?> StoredAsync(IPage page) =>
        page.EvaluateAsync<string?>("() => localStorage.getItem('rask.appearance')");

    /// <summary>The ground actually painted that way — a dark class over a white page is the real failure.</summary>
    private static async Task AssertGroundAsync(IPage page, bool dark)
    {
        var luminance = await page.EvaluateAsync<double>(
            Rgb + "(() => lum(getComputedStyle(document.documentElement).backgroundColor))()");

        Assert.True(
            dark ? luminance < 0.2 : luminance > 0.7,
            $"the {(dark ? "dark" : "light")} page painted the wrong ground (luminance {luminance:0.000}).");
    }

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

    // Luminance through a 1x1 canvas: Chromium reports a computed colour in the space it was AUTHORED in
    // (oklch, oklab), so parsing the string as rgb() scores NaN and every assertion on it passes.
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

    [GeneratedRegex(@"(^|\s)dark(\s|$)")]
    private static partial Regex DarkClass();
}
