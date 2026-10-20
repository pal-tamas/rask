using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     Flux's flag on the running site: the drawings are files the build copied into <c>wwwroot/flags</c>,
///     so what only a browser can show is that each one is really served and really drawn.
/// </summary>
[Collection(WasmExampleCollection.Name)]
public sealed class UiKitFlagTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    private const string Drawn = "img => img.complete && img.naturalWidth > 0";

    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Wasm";
    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task Every_flag_of_the_first_example_is_served_from_the_apps_own_files_and_drawn_three_by_two() => RunAsync(async () =>
    {
        var scope = await OpenAsync();
        var flags = scope.Locator("[data-testid='ui-flag-basic'] [data-ui-flag]");

        await Expect(flags).ToHaveCountAsync(7);

        for (var at = 0; at < 7; at++)
        {
            var image = flags.Nth(at).Locator("img");
            await image.ScrollIntoViewIfNeededAsync();
            await Expect(image).ToHaveJSPropertyAsync("complete", true);
            Assert.True(await image.EvaluateAsync<bool>(Drawn), $"flag {at} was not drawn: its file was not served.");
            Assert.Matches("/flags/[A-Z]{2}\\.svg\\?v=", await image.GetAttributeAsync("src") ?? "");
            var box = (await flags.Nth(at).BoundingBoxAsync())!;
            Assert.Equal(24, box.Width, 0.5);
            Assert.Equal(16, box.Height, 0.5);
        }
    });

    [Fact]
    public Task The_five_sizes_are_five_widths_and_a_circle_is_as_tall_as_it_is_wide() => RunAsync(async () =>
    {
        var scope = await OpenAsync();
        var sizes = scope.Locator("[data-testid='ui-flag-sizes'] [data-ui-flag]");
        var circle = scope.Locator("[data-testid='ui-flag-circle'] [data-ui-flag]").First;

        var widths = new List<double>();
        for (var at = 0; at < 5; at++)
        {
            widths.Add(Math.Round((await sizes.Nth(at).BoundingBoxAsync())!.Width, 1));
        }

        var round = (await circle.BoundingBoxAsync())!;

        Assert.Equal([48, 40, 32, 24, 20], widths);
        Assert.Equal(round.Width, round.Height, 0.5);
        Assert.True(await circle.Locator("img").EvaluateAsync<bool>(Drawn), "the circle's flag was not drawn.");
    });

    [Fact]
    public Task A_country_picked_in_the_select_shows_its_flag_in_the_button_and_reaches_the_page() => RunAsync(async () =>
    {
        var scope = await OpenAsync();
        var example = scope.Locator("[data-testid='ui-flag-country']");
        var button = example.Locator("button[role='combobox']");
        await button.ScrollIntoViewIfNeededAsync();
        await Expect(button.Locator("[data-ui-flag]")).ToHaveAttributeAsync("data-country", "US");

        await button.ClickAsync();
        await example.Locator("[role='option']", new LocatorLocatorOptions { HasText = "Canada" }).ClickAsync();

        await Expect(scope.Locator("[data-testid='ui-flag-country-says']")).ToHaveTextAsync("Ships to CA.");
        await Expect(button.Locator("[data-ui-flag]")).ToHaveAttributeAsync("data-country", "CA");
        await Expect(button.Locator("[data-ui-flag] img")).ToHaveJSPropertyAsync("complete", true);
        Assert.True(await button.Locator("[data-ui-flag] img").EvaluateAsync<bool>(Drawn), "the picked country's flag was not drawn.");
    });

    [Fact]
    public Task A_flag_is_decoration_until_it_is_given_words_and_a_source_of_the_apps_own_is_drawn_as_given() => RunAsync(async () =>
    {
        var scope = await OpenAsync();
        var named = scope.Locator("[data-testid='ui-flag-accessibility'] [data-ui-flag] img");
        var own = scope.Locator("[data-testid='ui-flag-custom'] [data-ui-flag]");

        await own.ScrollIntoViewIfNeededAsync();

        await Expect(named.First).ToHaveAttributeAsync("alt", "");
        await Expect(named.Last).ToHaveAttributeAsync("alt", "Canada");
        await Expect(own.Locator("img")).ToHaveAttributeAsync("alt", "Pennant");
        Assert.Null(await own.GetAttributeAsync("data-country"));
        Assert.StartsWith("data:image/svg+xml", await own.Locator("img").GetAttributeAsync("src") ?? "", StringComparison.Ordinal);
    });

    private async Task<ILocator> OpenAsync()
    {
        await OpenUiPageAsync("flag", "Flag");

        var scope = Page.Locator("main");
        await Expect(scope).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await scope.ScrollIntoViewIfNeededAsync();
        return scope;
    }
}
