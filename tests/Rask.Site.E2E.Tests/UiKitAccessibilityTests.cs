using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     What the kit's table, tabs and chart say to a screen reader and do for the keyboard, in a browser, on the WASM
///     site: <c>aria-sort</c> on the sorted heading, the scroll marks a sticky column's shadow is drawn on, Home and End
///     in a row of tabs, and a chart's drawing named and described by its rows — at a desktop's width and a phone's, in
///     the light and the dark (owner, 2026-11-10).
/// </summary>
public sealed class UiKitAccessibilityTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    // A desktop in the light, a phone in the dark: every test runs in both.
    private static readonly (int Width, int Height, ColorScheme Scheme)[] Screens =
    [
        (1280, 720, ColorScheme.Light),
        (390, 844, ColorScheme.Dark),
    ];

    protected override string BaseUrl => app.BaseUrl;

    protected override string FixtureName => "Wasm";

    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task The_sorted_heading_says_which_way_and_moves_with_a_keyboard_sort() => RunAsync(async () =>
    {
        foreach (var screen in Screens)
        {
            await OnScreenAsync(screen, "table", "Table");
            var table = Page.Locator("[data-testid='ui-table-sortable'] table");
            var date = table.Locator("th", new() { HasText = "Date" });
            var status = table.Locator("th", new() { HasText = "Status" });
            await table.ScrollIntoViewIfNeededAsync();

            await Expect(table.Locator("th[aria-sort]")).ToHaveCountAsync(1);
            await Expect(date).ToHaveAttributeAsync("aria-sort", "descending");
            await status.Locator("button").FocusAsync();
            await Page.Keyboard.PressAsync("Enter");
            await Expect(status).ToHaveAttributeAsync("aria-sort", "ascending");
            await Page.Keyboard.PressAsync("Enter");

            await Expect(status).ToHaveAttributeAsync("aria-sort", "descending");
            await Expect(table.Locator("th[aria-sort]")).ToHaveCountAsync(1);
            Assert.Null(await date.GetAttributeAsync("aria-sort"));
            Assert.Null(await table.Locator("th", new() { HasText = "Customer" }).GetAttributeAsync("aria-sort"));
        }
    });

    [Fact]
    public Task A_list_page_says_the_sort_its_address_holds() => RunAsync(async () =>
    {
        foreach (var screen in Screens)
        {
            await OnScreenAsync(screen, "table", "Table");
            var scope = Page.Locator("[data-testid='ui-table-list']");
            var supplier = scope.Locator("th", new() { HasText = "Supplier" });
            var city = scope.Locator("th", new() { HasText = "City" });
            await scope.ScrollIntoViewIfNeededAsync();

            await Expect(supplier).ToHaveAttributeAsync("aria-sort", "ascending");
            await city.Locator("button").FocusAsync();
            await Page.Keyboard.PressAsync("Enter");
            await Expect(city).ToHaveAttributeAsync("aria-sort", "ascending", new() { Timeout = 15_000 });
            await Page.Keyboard.PressAsync("Enter");

            await Expect(city).ToHaveAttributeAsync("aria-sort", "descending", new() { Timeout = 15_000 });
            Assert.Null(await supplier.GetAttributeAsync("aria-sort"));
        }
    });

    [Fact]
    public Task A_sticky_columns_scroll_area_is_marked_for_the_sides_that_hide_columns() => RunAsync(async () =>
    {
        foreach (var screen in Screens)
        {
            await OnScreenAsync(screen, "table", "Table");
            var area = Page.Locator("[data-testid='ui-table-sticky'] [data-rask-scroll-edges]");
            var id = area.Locator("tbody td").First;
            await area.ScrollIntoViewIfNeededAsync();
            const string shadow = "el => getComputedStyle(el, '::after').boxShadow";

            // At its start: the columns past its right edge are hidden, nothing is under the sticky first column.
            await Expect(area).ToHaveAttributeAsync("data-scrolled-left", "");
            await Expect(area).Not.ToHaveAttributeAsync("data-scrolled-right", "");
            Assert.Equal("none", await id.EvaluateAsync<string>(shadow));

            await area.EvaluateAsync("el => el.scrollTo(40, 0)");
            await Expect(area).ToHaveAttributeAsync("data-scrolled-right", "");
            await Expect(area).ToHaveAttributeAsync("data-scrolled-left", "");
            await Page.WaitForFunctionAsync("el => getComputedStyle(el, '::after').boxShadow !== 'none'", await id.ElementHandleAsync());

            await area.EvaluateAsync("el => el.scrollTo(el.scrollWidth, 0)");
            await Expect(area).ToHaveAttributeAsync("data-scrolled-right", "");
            await Expect(area).Not.ToHaveAttributeAsync("data-scrolled-left", "");

            // Its box made wide enough for every column: nothing is hidden, and the shadow goes.
            await area.EvaluateAsync("el => { el.parentElement.style.maxWidth = 'none'; el.parentElement.style.width = '3000px'; }");
            await Expect(area).Not.ToHaveAttributeAsync("data-scrolled-right", "");
            await Expect(area).Not.ToHaveAttributeAsync("data-scrolled-left", "");
            Assert.Equal("none", await id.EvaluateAsync<string>(shadow));
        }
    });

    [Fact]
    public Task A_table_without_a_sticky_column_asks_for_no_scroll_marks() => RunAsync(async () =>
    {
        await OpenUiPageAsync("table", "Table");

        var marked = await Page.Locator("[data-testid='ui-table-intro'] [data-rask-scroll-edges]").CountAsync();

        Assert.Equal(0, marked);
        await Expect(Page.Locator("[data-testid='ui-table-sticky'] [data-rask-scroll-edges]")).ToHaveCountAsync(1);
    });

    [Fact]
    public Task Home_and_End_select_the_first_and_last_tab_from_the_keyboard() => RunAsync(async () =>
    {
        foreach (var screen in Screens)
        {
            await OnScreenAsync(screen, "tabs", "Tabs");
            var scope = Page.Locator("[data-testid='ui-tabs']");
            var state = scope.Locator("[data-testid='ui-tabs-state']");
            var (profile, billing) = (Tab(scope, "Profile"), Tab(scope, "Billing"));
            await profile.FocusAsync();

            await Page.Keyboard.PressAsync("End");
            await Expect(state).ToHaveTextAsync("Selected: billing.");
            await Expect(billing).ToBeFocusedAsync();
            await Expect(billing).ToHaveAttributeAsync("aria-selected", "true");
            await Page.Keyboard.PressAsync("Home");

            await Expect(state).ToHaveTextAsync("Selected: profile.");
            await Expect(profile).ToBeFocusedAsync();
            await Expect(profile).ToHaveAttributeAsync("aria-selected", "true");
            await Expect(billing).ToHaveAttributeAsync("tabindex", "-1");
        }
    });

    [Fact]
    public Task Home_and_End_pass_over_a_disabled_tab() => RunAsync(async () =>
    {
        foreach (var screen in Screens)
        {
            await OnScreenAsync(screen, "tabs", "Tabs");
            var tabs = Page.Locator("[data-testid='ui-tabs-disabled'] [role='tab']");
            var (open, archived, failed) = (tabs.Nth(0), tabs.Nth(1), tabs.Nth(2));
            await Expect(archived).ToBeDisabledAsync();
            await open.FocusAsync();

            await Page.Keyboard.PressAsync("End");
            await Expect(failed).ToBeFocusedAsync();
            await Page.Keyboard.PressAsync("Home");

            await Expect(open).ToBeFocusedAsync();
            await Expect(open).ToHaveAttributeAsync("aria-selected", "true");
            await Expect(archived).ToHaveAttributeAsync("aria-selected", "false");
        }
    });

    [Fact]
    public Task A_charts_drawing_is_an_image_named_and_described_by_its_rows() => RunAsync(async () =>
    {
        foreach (var screen in Screens)
        {
            await OnScreenAsync(screen, "chart", "Chart");
            var line = Page.Locator("[data-testid='ui-chart-line'] [data-ui-chart] svg[role='img']");
            var lines = Page.Locator("[data-testid='ui-chart-lines'] [data-ui-chart] svg[role='img']");
            await line.ScrollIntoViewIfNeededAsync();

            await Expect(line).ToHaveAttributeAsync("aria-label", new System.Text.RegularExpressions.Regex(
                "^Line chart: Visitors\\. 16 data points, .+ to .+\\. Values from 115 to 300\\.$"), new() { Timeout = 15_000 });
            var described = await line.GetAttributeAsync("aria-describedby");
            var table = Page.Locator($"table#{described}");
            await Expect(table.Locator("tbody tr")).ToHaveCountAsync(16);
            await Expect(table.Locator("thead th")).ToHaveTextAsync(["Date", "Visitors"]);
            var box = (await table.BoundingBoxAsync())!;
            Assert.True(box.Width <= 1 && box.Height <= 1, $"the rows' table shows: {box.Width}x{box.Height}.");
            await lines.ScrollIntoViewIfNeededAsync();
            await Expect(lines).ToHaveAttributeAsync("aria-label", new System.Text.RegularExpressions.Regex(
                "^Line chart: Twitter, Facebook, Instagram\\. 5 data points, .+ Values from 9% to 59%\\.$"), new() { Timeout = 15_000 });
            await Expect(Page.Locator("[data-testid='ui-chart-pie'] svg[role='img']").First).ToHaveAttributeAsync(
                "aria-label", new System.Text.RegularExpressions.Regex("^(Pie|Donut) chart: Value\\. \\d+ slices"), new() { Timeout = 15_000 });
        }
    });

    private async Task OnScreenAsync((int Width, int Height, ColorScheme Scheme) screen, string slug, string heading)
    {
        await Page.SetViewportSizeAsync(screen.Width, screen.Height);
        await Page.EmulateMediaAsync(new PageEmulateMediaOptions { ColorScheme = screen.Scheme });
        await OpenUiPageAsync(slug, heading);
    }

    private static ILocator Tab(ILocator scope, string name) => scope.GetByRole(AriaRole.Tab, new LocatorGetByRoleOptions { Name = name });
}
