using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     The kit's filters on the running site, in the browser's own WebAssembly app: the panel opened by its button,
///     a pick that moves the address and arrives as a badge, and the badges taken off again.
/// </summary>
public sealed class UiKitFiltersTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    private const string Panel = "[data-testid='ui-filters'] dialog[data-ui-flyout]";

    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Wasm";
    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task A_pick_in_the_panel_moves_the_address_and_its_badge_takes_it_off_again() => RunAsync(async () =>
    {
        await OpenUiPageAsync("filters", "Filters");
        var example = Page.Locator("[data-testid='ui-filters']");
        await example.ScrollIntoViewIfNeededAsync();
        await Expect(example.Locator("[data-rask-skeleton] tbody tr:not(:has([data-ui-skeleton-line]))")).ToHaveCountAsync(5);

        await example.Locator("[data-ui-filters] [data-ui-modal-trigger] button").ClickAsync();
        await Page.Locator(Panel + " [data-ui-select-button]").First.ClickAsync();
        await Page.Locator(Panel + " [data-ui-option]").Filter(new() { HasTextString = "Grace Hopper" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"driver=grace"));
        await Page.Locator(Panel + " button").Filter(new() { HasTextString = "Close" }).ClickAsync();

        await Expect(example.Locator("[data-ui-filters-badges] [data-ui-badge]")).ToHaveTextAsync("Driver: Grace Hopper");
        await Expect(example.Locator("[data-rask-skeleton] tbody tr:not(:has([data-ui-skeleton-line]))")).ToHaveCountAsync(2);

        await example.Locator("[aria-label='Remove filter Driver']").ClickAsync();

        await Expect(example.Locator("[data-ui-filters-badges]")).ToHaveCountAsync(0);
        await Expect(example.Locator("[data-rask-skeleton] tbody tr:not(:has([data-ui-skeleton-line]))")).ToHaveCountAsync(5);
        Assert.DoesNotContain("driver=", Page.Url, StringComparison.Ordinal);
    });

    [Fact]
    public Task On_a_desktop_long_and_many_names_keep_the_header_in_the_page() => RunAsync(async () =>
    {
        await Page.SetViewportSizeAsync(1280, 800);

        await AssertLongAndManyNamesAsync(phone: false);
    });

    [Fact]
    public Task On_a_phone_long_and_many_names_keep_the_header_in_the_page() => RunAsync(() => OnAPhoneAsync(() => AssertLongAndManyNamesAsync(phone: true)));

    // The "Many names, long words" example: no sideways scroll, the route ellipsed with its words in the title, the
    // vans counted with every one in a tooltip (hover, or a tap on a phone) kept on the screen, the header's three
    // buttons one height, and a cross that still takes the cut-off badge off.
    private async Task AssertLongAndManyNamesAsync(bool phone)
    {
        await OpenUiPageAsync("filters", "Filters");
        var example = Page.Locator("[data-testid='ui-filters-long']");
        await example.ScrollIntoViewIfNeededAsync();
        var route = example.Locator("[data-ui-filters-badges] [data-ui-badge][title]");
        var vans = example.Locator("[data-ui-filters-badges] > [data-ui-tooltip]");
        await Expect(route).ToHaveCountAsync(1);

        var overflow = await Page.EvaluateAsync<double>("() => document.documentElement.scrollWidth - innerWidth");
        var cut = await route.Locator("span").EvaluateAsync<bool>("s => s.scrollWidth > s.clientWidth");
        var title = await route.GetAttributeAsync("title");
        var heights = await example.EvaluateAsync<double[]>(
            "e => [e.querySelector('[data-ui-filters] [data-ui-modal-trigger] button'), e.querySelector('[data-ui-dropdown] button'), e.querySelector('[data-ui-toggle]')].map(b => b.getBoundingClientRect().height)");

        Assert.True(overflow <= 0, $"the page scrolls {overflow}px sideways");
        Assert.True(cut, "the route's words are not cut off");
        Assert.StartsWith("Route: Budapest, Tatabánya", title, StringComparison.Ordinal);
        Assert.Equal([40d, 40d, 40d], heights);
        await Expect(vans.Locator("[data-ui-badge] > span")).ToHaveTextAsync("Vehicles: 40 selected");
        Assert.Null(await vans.Locator("[data-ui-badge]").GetAttributeAsync("title"));

        var words = vans.Locator("[data-ui-badge] > span");
        if (phone)
        {
            await words.TapAsync();
        }
        else
        {
            await words.HoverAsync();
        }

        var tooltip = vans.Locator("[data-ui-tooltip-content]");
        await Expect(tooltip).ToBeVisibleAsync();
        await Expect(tooltip).ToContainTextAsync("Van 01, Van 02");
        await Expect(tooltip).ToContainTextAsync("Van 40");
        var box = await tooltip.EvaluateAsync<double[]>("t => { const r = t.getBoundingClientRect(); return [r.left, r.right, r.width, innerWidth, r.height]; }");
        Assert.True(box[0] >= 0 && box[1] <= box[3], $"the tooltip runs off the screen: {box[0]}..{box[1]} of {box[3]}");
        Assert.True(box[2] < box[3] - 20 && box[4] > 40, $"the tooltip is {box[2]} x {box[4]}px, not wrapped");

        await route.Locator("[data-ui-badge-close]").ClickAsync();

        await Expect(route).ToHaveCountAsync(0);
    }

    [Fact]
    public Task A_lazy_modal_holds_nothing_until_it_opens() => RunAsync(async () =>
    {
        await OpenUiPageAsync("modal", "Modal");
        var example = Page.Locator("[data-testid='ui-modal-lazy']");
        await example.ScrollIntoViewIfNeededAsync();
        await Expect(example.Locator("dialog [data-ui-input]")).ToHaveCountAsync(0);

        await example.Locator("button").Filter(new() { HasTextString = "Edit profile" }).ClickAsync();

        await Expect(example.Locator("dialog[open] [data-ui-input]")).ToBeVisibleAsync();
    });
}
