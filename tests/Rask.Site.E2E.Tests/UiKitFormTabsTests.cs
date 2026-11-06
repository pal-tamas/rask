using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     <c>Ui.FormTabs</c> on the UI tab: a red count on each tab for the invalid fields it holds, the first such tab
///     opened by every refused save, the counts following each correction the next save carries, and the save going
///     through once all are made.
/// </summary>
public sealed class UiKitFormTabsTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;

    protected override string FixtureName => "Wasm";

    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task A_refused_save_counts_each_tabs_fields_opens_the_first_and_the_next_save_carries_each_correction() => RunAsync(async () =>
    {
        await OpenUiPageAsync("form-tabs", "Form tabs");
        var scope = Page.Locator("[data-testid='ui-form-tabs']");
        var details = scope.GetByRole(AriaRole.Tab, new() { Name = "Details" });
        var address = scope.GetByRole(AriaRole.Tab, new() { Name = "Address" });
        var notes = scope.GetByRole(AriaRole.Tab, new() { Name = "Notes" });
        await scope.ScrollIntoViewIfNeededAsync();
        var clean = await scope.Locator("[data-ui-form-tab-count]").CountAsync();
        await notes.ClickAsync();
        await Expect(notes).ToHaveAttributeAsync("aria-selected", "true");

        await scope.Locator("#uft-save").ClickAsync();
        await Expect(details).ToHaveAttributeAsync("aria-selected", "true", new() { Timeout = 10_000 });
        await Expect(details.Locator("[data-ui-form-tab-count]")).ToHaveAttributeAsync("data-ui-form-tab-count", "1");
        await Expect(address.Locator("[data-ui-form-tab-count]")).ToHaveAttributeAsync("data-ui-form-tab-count", "2");
        var badge = await address.Locator("[data-ui-badge]").GetAttributeAsync("class");
        await Expect(details).ToHaveAccessibleNameAsync("Details Fields to correct: 1");
        await scope.Locator("#uft-email").FillAsync("ops@aminoff.example");
        await scope.Locator("#uft-save").ClickAsync();
        await Expect(address).ToHaveAttributeAsync("aria-selected", "true", new() { Timeout = 10_000 });
        await Expect(details.Locator("[data-ui-form-tab-count]")).ToHaveCountAsync(0);
        await Expect(address.Locator("[data-ui-form-tab-count]")).ToHaveAttributeAsync("data-ui-form-tab-count", "2");
        await scope.Locator("#uft-street").FillAsync("Storgata 1");
        await scope.Locator("#uft-city").FillAsync("Oslo");
        await scope.Locator("#uft-save").ClickAsync();

        await Expect(Page.Locator("#uft-said")).ToHaveTextAsync("Saved Aminoff Freight, Oslo.", new() { Timeout = 10_000 });
        await Expect(scope.Locator("[data-ui-form-tab-count]")).ToHaveCountAsync(0);
        Assert.Equal(0, clean);
        Assert.Contains("text-red-700", badge, StringComparison.Ordinal);
    });

    [Fact]
    public Task The_tab_row_scrolls_inside_its_box_on_a_phone_and_the_arrows_move_between_tabs() => RunAsync(async () =>
    {
        await OpenUiPageAsync("form-tabs", "Form tabs");
        var scope = Page.Locator("[data-testid='ui-form-tabs']");
        var details = scope.GetByRole(AriaRole.Tab, new() { Name = "Details" });
        await Page.SetViewportSizeAsync(390, 844);
        await scope.ScrollIntoViewIfNeededAsync();

        var widths = await Page.EvaluateAsync<double[]>("() => [document.documentElement.scrollWidth, document.documentElement.clientWidth]");
        await details.FocusAsync();
        await Page.Keyboard.PressAsync("ArrowRight");

        await Expect(scope.GetByRole(AriaRole.Tab, new() { Name = "Address" })).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(scope.Locator("#uft-street")).ToBeVisibleAsync();
        Assert.True(widths[0] <= widths[1], $"the page is wider than a phone: {widths[0]} > {widths[1]}");
    });
}
