using Rask.Site.E2E.Tests.Infrastructure;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     <c>Ui.RowActions</c>' long menu on the WASM site (the row actions page's "A long menu"): eighteen actions in named
///     groups, opened from a row at the top, in the middle and at the bottom of a short phone and of a desktop. The menu's
///     box is inside the viewport, it opens at its first action, scrolls to its last, and the arrow keys bring every action
///     they reach into view. The same reading as the Server suite's <c>ATallRowMenuTests</c>.
/// </summary>
public sealed class UiKitLongMenuTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;

    protected override string FixtureName => "Wasm";

    protected override string ServerLog => app.ServerLog;

    [Theory]
    [InlineData(390, 600, TallMenus.Top)]
    [InlineData(390, 600, TallMenus.Middle)]
    [InlineData(390, 600, TallMenus.Bottom)]
    [InlineData(1280, 800, TallMenus.Top)]
    [InlineData(1280, 800, TallMenus.Middle)]
    [InlineData(1280, 800, TallMenus.Bottom)]
    public Task A_long_row_menu_opened_by_a_press_fits_the_viewport_and_scrolls_to_its_last_action(int width, int height, string where) => RunAsync(async () =>
    {
        await OpenAsync(width, height);
        var trigger = await TallMenus.TriggerAsync(Rows, where);

        await trigger.ClickAsync();
        var opened = await TallMenus.OpenedAsync(Page);
        var reach = await TallMenus.ScrolledThroughAsync(Page);

        TallMenus.AssertFits(opened, width, height);
        Assert.Equal(0d, opened.ScrollTop);
        Assert.True(opened.FirstInside, $"the first action is not in the menu's box as it opens: {opened}");
        Assert.Equal(TallMenus.Rows, reach.Count);
        Assert.All(reach, inside => Assert.True(inside, $"an action scrolled into view is not in the menu's box: {string.Join(",", reach)}"));
    });

    [Theory]
    [InlineData(390, 600, TallMenus.Top)]
    [InlineData(390, 600, TallMenus.Middle)]
    [InlineData(390, 600, TallMenus.Bottom)]
    [InlineData(1280, 800, TallMenus.Top)]
    [InlineData(1280, 800, TallMenus.Middle)]
    [InlineData(1280, 800, TallMenus.Bottom)]
    public Task A_long_row_menu_opened_by_the_keyboard_focuses_its_first_action_and_the_arrows_keep_each_in_view(int width, int height, string where) => RunAsync(async () =>
    {
        await OpenAsync(width, height);
        var trigger = await TallMenus.TriggerAsync(Rows, where);

        await trigger.FocusAsync();
        await Page.Keyboard.PressAsync("ArrowDown");
        var opened = await TallMenus.OpenedAsync(Page);
        var walk = await TallMenus.WalkedAsync(Page);

        TallMenus.AssertFits(opened, width, height);
        Assert.True(opened.FirstFocused, $"the first action does not have focus: {opened}");
        Assert.True(opened.FirstInside, $"the first action is not in the menu's box as it opens: {opened}");
        Assert.Equal(2 * TallMenus.Rows - 1, walk.Count);
        Assert.All(walk, step => Assert.True(step.Inside, $"a focused action is out of the menu's box: {string.Join(" | ", walk)}"));
        Assert.Equal(TallMenus.Rows, walk.Select(step => step.Key).Distinct().Count());
    });

    private Microsoft.Playwright.ILocator Rows => Page.Locator("[data-testid='ui-row-actions-long'] #url-pumps tbody tr");

    private async Task OpenAsync(int width, int height)
    {
        await Page.SetViewportSizeAsync(width, height);
        await OpenUiPageAsync("row-actions", "Row actions");
        await Microsoft.Playwright.Assertions.Expect(Rows).ToHaveCountAsync(10);
    }
}
