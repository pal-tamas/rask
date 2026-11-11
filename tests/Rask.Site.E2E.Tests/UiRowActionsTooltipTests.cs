using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     The ⋯ of <c>Ui.RowActions</c> on the UI tab wears the tooltip "Actions". Its menu, opened with the mouse and closed
///     by a pick, a click outside or Escape, hands focus back to it — and that is no keyboard navigation, so the tooltip
///     stays shut. A Tab onto the ⋯ shows it, inside the viewport, over the table's scroller and the sticky cell.
/// </summary>
public sealed class UiRowActionsTooltipTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;

    protected override string FixtureName => "Wasm";

    protected override string ServerLog => app.ServerLog;

    [Theory]
    [MemberData(nameof(TooltipWalk.Closes), MemberType = typeof(TooltipWalk))]
    public Task A_menu_the_mouse_closed_hands_focus_back_to_its_button_without_its_tooltip(int width, string close) => RunAsync(async () =>
    {
        await Page.SetViewportSizeAsync(width, 844);
        await OpenUiPageAsync("row-actions", "Row actions");
        var row = Page.Locator("[data-testid='ui-row-actions-menu'] #urm-suppliers tbody tr").Filter(new() { HasText = "Bothman" });
        var trigger = row.GetByRole(AriaRole.Button, new() { Name = "Actions" });
        var menu = row.Locator("[role=menu]");
        await trigger.ScrollIntoViewIfNeededAsync();

        await TooltipWalk.CloseByMouseShowsNoTooltipAsync(Page, trigger, menu, menu.GetByRole(AriaRole.Menuitem, new() { Name = "Choose" }), row.Locator("td").First, close);
    });

    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    public Task A_Tab_onto_the_button_shows_its_tooltip_inside_the_viewport_over_the_scroller(int width) => RunAsync(async () =>
    {
        await Page.SetViewportSizeAsync(width, 844);
        await OpenUiPageAsync("row-actions", "Row actions");
        var trigger = Page.Locator("[data-testid='ui-row-actions-menu'] #urm-suppliers tbody tr").First.GetByRole(AriaRole.Button, new() { Name = "Actions" });
        await trigger.ScrollIntoViewIfNeededAsync();

        await TooltipWalk.TabShowsTooltipInViewAsync(Page, trigger);
    });
}
