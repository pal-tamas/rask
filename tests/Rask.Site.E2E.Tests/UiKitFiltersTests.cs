using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     The kit's filters on the running site, in the browser's own WebAssembly app: the panel opened by its button,
///     a pick that moves the address and arrives as a badge, and the badges taken off again.
/// </summary>
[Collection(WasmExampleCollection.Name)]
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
