using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     The kit's Feedback components, in a browser.
/// </summary>
[Collection(WasmExampleCollection.Name)]
public sealed class UiKitFeedbackTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Wasm";
    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task Every_feedback_component_renders_with_a_real_size() => RunAsync(async () =>
    {
        await OpenAsync();

        foreach (var id in new[]
                 {
                     "ui-alert", "ui-loading", "ui-progress", "ui-tooltip", "ui-skeleton", "ui-toast",
                 })
        {
            var node = Page.Locator($"[data-testid='{id}']");
            await Expect(node).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

            var box = await node.BoundingBoxAsync();
            Assert.NotNull(box);
            Assert.True(box!.Height > 0, $"{id} rendered with zero height.");
        }
    });

    [Fact]
    public Task Every_loading_shape_is_drawn_rather_than_being_an_empty_span() => RunAsync(async () =>
    {
        await OpenAsync();

        // `loading` on its own is an unstyled span. Six shapes, six non-zero boxes: this is the check
        // that a shape class reached the compiled sheet at all.
        var indicators = Page.Locator("[data-testid='ui-loading'] .loading");
        await Expect(indicators).ToHaveCountAsync(6);

        for (var i = 0; i < 6; i++)
        {
            var box = await indicators.Nth(i).BoundingBoxAsync();
            Assert.NotNull(box);
            Assert.True(box!.Width > 0 && box.Height > 0, $"loading indicator {i} drew nothing.");
        }
    });

    [Fact]
    public Task The_progress_element_reports_its_own_value() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-progress']");
        var bar = scope.Locator("progress").First;

        await Expect(bar).ToHaveAttributeAsync("value", "62");

        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "+10" }).ClickAsync();

        // A real <progress>, so the value is the element's own rather than a width the kit painted.
        await Expect(bar).ToHaveAttributeAsync("value", "72");
    });

    [Fact]
    public Task A_toast_appears_on_demand_and_can_be_dismissed() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-toast']");

        // Scoped to the toast's own section, not the page. Ui.Alert renders role="status" for any tone
        // that is not an error — which is right, an outcome should be announced politely rather than
        // interrupting — and this page has an alert reading "Saved." as well, so a page-wide locator
        // matched two elements and went on matching the alert after the toast was dismissed.
        var toast = scope.Locator("[role='status']");

        // No assertion that it starts absent, deliberately. The harness re-runs this body on a boot
        // failure (RaceAgainstBootFailureAsync), and the second attempt gets a page the first one had
        // already clicked Save on — so "there is no toast yet" is a claim about the harness rather than
        // about the component. What the component owes is the TRANSITION, which is what is asserted.
        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save" }).ClickAsync();
        await Expect(toast).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        await toast.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Dismiss" })
            .ClickAsync();
        await Expect(toast).ToHaveCountAsync(0);
    });

    [Fact]
    public Task A_tooltip_shows_beside_its_trigger_on_hover_and_names_an_icon_button() => RunAsync(async () =>
    {
        await OpenTooltipsAsync();

        var tooltip = Page.Locator("[data-testid='ui-tooltip'] > [data-ui-tooltip]").First;
        var trigger = tooltip.Locator("button");
        var content = tooltip.Locator("[data-ui-tooltip-content]");
        await Expect(content).ToBeHiddenAsync();
        await trigger.HoverAsync();

        // Shown at once, above the trigger and centred on it, Flux's 5px away.
        await Expect(content).ToBeVisibleAsync();
        await Expect(content).ToHaveTextAsync("Settings");
        var gap = await tooltip.EvaluateAsync<double[]>(
            @"el => { const t = el.firstElementChild.getBoundingClientRect(), c = el.lastElementChild.getBoundingClientRect();
                      return [t.top - c.bottom, (c.left + c.right) / 2 - (t.left + t.right) / 2]; }");
        Assert.Equal(5, gap[0], 1);
        Assert.Equal(0, gap[1], 1);

        // The association is what a screen reader hears: with no text of its own, the button is named by it.
        await Expect(trigger).ToHaveAttributeAsync("aria-labelledby", await content.GetAttributeAsync("id") ?? "");
        await Expect(trigger).ToHaveAccessibleNameAsync("Settings");
        await Expect(content).ToHaveAttributeAsync("role", "tooltip");

        await Page.Mouse.MoveAsync(2, 2);
        await Expect(content).ToBeHiddenAsync();
    });

    [Fact]
    public Task A_tooltip_shows_on_keyboard_focus_and_Escape_dismisses_it() => RunAsync(async () =>
    {
        await OpenTooltipsAsync();

        var tooltip = Page.Locator("[data-testid='ui-tooltip'] > [data-ui-tooltip]").First;
        var content = tooltip.Locator("[data-ui-tooltip-content]");
        // A key first, so the focus that follows is the keyboard's (:focus-visible).
        await Page.Keyboard.PressAsync("Shift");
        await tooltip.Locator("button").FocusAsync();

        await Expect(content).ToBeVisibleAsync();

        await Page.Keyboard.PressAsync("Escape");
        await Expect(content).ToBeHiddenAsync();
    });

    [Fact]
    public Task A_tooltip_opens_on_each_side_it_is_asked_for() => RunAsync(async () =>
    {
        await OpenTooltipsAsync();

        var tooltips = Page.Locator("[data-testid='ui-tooltip-positions'] [data-ui-tooltip]");
        const string Offset =
            @"el => { const t = el.firstElementChild.getBoundingClientRect(), c = el.lastElementChild.getBoundingClientRect();
                      return [Math.round(c.left - t.right), Math.round(c.top - t.bottom), Math.round(t.left - c.right), Math.round(t.top - c.bottom)]; }";

        // right · below · left · above: the one that reads 5 is the side it opened on.
        foreach (var (index, side) in new[] { (0, 3), (1, 0), (2, 1), (3, 2) })
        {
            await tooltips.Nth(index).Locator("button").HoverAsync();
            await Expect(tooltips.Nth(index).Locator("[data-ui-tooltip-content]")).ToBeVisibleAsync();
            Assert.Equal(5, (await tooltips.Nth(index).EvaluateAsync<int[]>(Offset))[side]);
        }
    });

    [Fact]
    public Task A_toggleable_tooltip_opens_on_a_click_a_disabled_button_still_explains_itself_and_a_shortcut_is_shown() => RunAsync(async () =>
    {
        await OpenTooltipsAsync();

        var scope = Page.Locator("[data-testid='ui-tooltip']");

        // Toggleable: a tap is a click, and a hover is nothing — the only tooltip a phone ever shows.
        var info = scope.Locator("[data-testid='ui-tooltip-info'] [data-ui-tooltip]");
        var panel = info.Locator("[data-ui-tooltip-content]");
        await info.Locator("button").HoverAsync();
        await Expect(panel).ToBeHiddenAsync();
        await info.Locator("button").ClickAsync();
        await Expect(panel).ToBeVisibleAsync();
        await Expect(panel.Locator("p")).ToHaveCountAsync(2);
        await Page.Keyboard.PressAsync("Escape");
        await Expect(panel).ToBeHiddenAsync();

        // A disabled button takes no pointer events, so the hover lands on the wrapper and the tip still shows.
        var disabled = scope.Locator("[data-ui-tooltip]", new LocatorLocatorOptions { Has = Page.Locator("button[disabled]") });
        await disabled.HoverAsync();
        await Expect(disabled.Locator("[data-ui-tooltip-content]")).ToBeVisibleAsync();

        // The shortcut follows the text, in the tooltip.
        await Expect(scope.Locator("[data-ui-tooltip-content] span")).ToHaveTextAsync("⌘S");
    });

    // Mid-viewport, so a tooltip has room on the side it was asked for and does not flip.
    private async Task OpenTooltipsAsync()
    {
        await OpenAsync();
        await Page.Locator("[data-testid='ui-tooltip']").EvaluateAsync("el => el.scrollIntoView({ block: 'center' })");
    }

    private async Task OpenAsync()
    {
        await Page.GotoAsync(Docs);
        await Expect(Page.Locator(".side-nav a.side-nav-link[aria-current='page']").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await ClickSidebar("Feedback");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Feedback",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
    }
}
