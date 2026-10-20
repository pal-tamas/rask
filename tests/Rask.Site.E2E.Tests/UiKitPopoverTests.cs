using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     Flux's popover on the running site: a dropdown around a trigger and a <c>Ui.Popover</c>, under a real
///     pointer and real keys.
/// </summary>
/// <remarks>
///     What a unit test cannot see: that the browser places the panel beside its trigger, that the runtime's
///     hooks lock the page, follow the pointer and keep Tab honest, and that the page hears each of it.
/// </remarks>
[Collection(WasmExampleCollection.Name)]
public sealed class UiKitPopoverTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    private const string PagePointer = "getComputedStyle(document.documentElement).pointerEvents";

    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Wasm";
    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task A_pressed_popover_opens_under_its_trigger_locks_the_page_and_Escape_hands_focus_back() => RunAsync(async () =>
    {
        var scope = await OpenAsync();
        var trigger = Trigger(scope, "Options");
        var panel = Panel(trigger);
        await Expect(panel).ToBeHiddenAsync();

        await trigger.ClickAsync();

        // Below the trigger, start edges together, Flux's 5px apart — placed by the browser.
        await Expect(panel).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(trigger).ToHaveAttributeAsync("aria-expanded", "true");
        await Expect(panel).ToHaveAttributeAsync("data-open", "");
        var button = (await trigger.BoundingBoxAsync())!;
        var box = (await panel.BoundingBoxAsync())!;
        Assert.Equal(button.X, box.X, 1.0);
        Assert.Equal(button.Y + button.Height + 5, box.Y, 1.0);
        // The page behind takes no pointer while it is open, and what is in the panel still does.
        Assert.Equal("none", await Page.EvaluateAsync<string>(PagePointer));
        await panel.GetByText("Gallery").ClickAsync();
        await Expect(scope.Locator("[data-testid='ui-popover-says']")).ToContainTextAsync("viewed as gallery");
        await Expect(panel).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(panel).ToBeHiddenAsync();
        await Expect(trigger).ToBeFocusedAsync();
        await Expect(panel).Not.ToHaveAttributeAsync("data-open", "");
        Assert.Equal("auto", await Page.EvaluateAsync<string>(PagePointer));
    });

    [Fact]
    public Task Tab_walks_from_the_trigger_into_the_panel_and_leaving_the_trigger_closes_it() => RunAsync(async () =>
    {
        var scope = await OpenAsync();
        var trigger = Trigger(scope, "Options");
        var panel = Panel(trigger);

        await trigger.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");

        // Focus stays on the trigger as it opens: the panel takes none.
        await Expect(panel).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(trigger).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Tab");
        Assert.True(await panel.EvaluateAsync<bool>("panel => panel.contains(document.activeElement)"), "Tab did not walk into the panel.");
        await Page.Keyboard.PressAsync("Shift+Tab");
        await Expect(trigger).ToBeFocusedAsync();
        await Expect(panel).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Shift+Tab");
        await Expect(panel).ToBeHiddenAsync();
    });

    [Fact]
    public Task A_press_outside_closes_a_popover_and_goes_no_further() => RunAsync(async () =>
    {
        var scope = await OpenAsync();
        var trigger = Trigger(scope, "Categories");
        var panel = Panel(trigger);
        var other = Trigger(scope, "Feedback");

        await trigger.ClickAsync();
        await Expect(panel).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        var box = (await other.BoundingBoxAsync())!;
        await Page.Mouse.ClickAsync(box.X + 10, box.Y + 10);

        // The press closed the open popover, and the button under it was not pressed: its own stays shut.
        await Expect(panel).ToBeHiddenAsync();
        await Expect(scope.Locator("[data-ui-popover]:popover-open")).ToHaveCountAsync(0);
    });

    [Fact]
    public Task The_category_picker_counts_what_is_ticked_and_stays_open_while_it_is_used() => RunAsync(async () =>
    {
        var scope = await OpenAsync();
        var trigger = Trigger(scope, "Categories");
        var panel = Panel(trigger);
        var count = scope.Locator("[data-testid='ui-popover-count']");
        await Expect(count).ToHaveTextAsync("3");

        await trigger.ClickAsync();
        await panel.GetByText("Horror").ClickAsync();

        await Expect(count).ToHaveTextAsync("4");
        await panel.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Clear all" }).ClickAsync();
        await Expect(count).ToHaveTextAsync("0");
        await Expect(panel).ToBeVisibleAsync();
    });

    [Fact]
    public Task A_state_driven_popover_is_closed_by_the_page_and_opens_again() => RunAsync(async () =>
    {
        var scope = await OpenAsync();
        var trigger = Trigger(scope, "Feedback");
        var panel = Panel(trigger);

        await trigger.ClickAsync();
        await Expect(panel).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await panel.Locator("textarea").FillAsync("The save button is slow");
        await panel.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Submit" }).ClickAsync();

        // C# set Open to false, and the runtime hid the popover to match.
        await Expect(panel).ToBeHiddenAsync(new LocatorAssertionsToBeHiddenOptions { Timeout = 10_000 });
        await Expect(scope.Locator("[data-testid='ui-popover-sent']")).ToContainTextAsync("The save button is slow");
        // The page heard that it closed, so the next press opens it again — and Escape still closes it.
        await trigger.ClickAsync();
        await Expect(panel).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Page.Keyboard.PressAsync("Escape");
        await Expect(panel).ToBeHiddenAsync();
        await trigger.ClickAsync();
        await Expect(panel).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task A_hovered_popover_opens_under_the_pointer_locks_nothing_and_closes_when_the_pointer_leaves() => RunAsync(async () =>
    {
        var scope = await OpenAsync();
        var trigger = Trigger(scope, "Caleb Porzio");
        var panel = Panel(trigger);

        await trigger.HoverAsync();

        // Flux's example: 10px under the trigger, and 16px back past its start edge.
        await Expect(panel).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        var button = (await trigger.BoundingBoxAsync())!;
        var box = (await panel.BoundingBoxAsync())!;
        Assert.Equal(button.X - 16, box.X, 1.0);
        Assert.Equal(button.Y + button.Height + 10, box.Y, 1.0);
        Assert.Equal("auto", await Page.EvaluateAsync<string>(PagePointer));
        // Straight onto the panel, which keeps it. Then away from both.
        await Page.Mouse.MoveAsync(box.X + (box.Width / 2), box.Y + (box.Height / 2));
        await Expect(panel).ToBeVisibleAsync();
        // A move is looked at once in 100 ms, as on Flux, and the one onto the panel was: the pointer rests first.
        await Page.WaitForTimeoutAsync(150);
        await Page.Mouse.MoveAsync(box.X + box.Width + 80, box.Y + box.Height + 80);
        await Expect(panel).ToBeHiddenAsync();
    });

    [Fact]
    public Task Each_position_gap_and_offset_lands_where_Flux_puts_it() => RunAsync(async () =>
    {
        var scope = await OpenAsync();

        var above = await OpenedAsync(scope, "Top Start");
        var beside = await OpenedAsync(scope, "Right Center");
        var ended = await OpenedAsync(scope, "Bottom End");
        var apart = await OpenedAsync(scope, "Gap: 16px");
        var shifted = await OpenedAsync(scope, "Offset: 32px");

        Assert.Equal(above.Trigger.X, above.Panel.X, 1.0);
        Assert.Equal(above.Trigger.Y - 5, above.Panel.Y + above.Panel.Height, 1.0);
        Assert.Equal(beside.Trigger.X + beside.Trigger.Width + 5, beside.Panel.X, 1.0);
        Assert.Equal(beside.Trigger.Y + (beside.Trigger.Height / 2), beside.Panel.Y + (beside.Panel.Height / 2), 1.0);
        Assert.Equal(ended.Trigger.X + ended.Trigger.Width, ended.Panel.X + ended.Panel.Width, 1.0);
        Assert.Equal(ended.Trigger.Y + ended.Trigger.Height + 5, ended.Panel.Y, 1.0);
        Assert.Equal(apart.Trigger.Y + apart.Trigger.Height + 16, apart.Panel.Y, 1.0);
        Assert.Equal(shifted.Trigger.X + 32, shifted.Panel.X, 1.0);
    });

    [Fact]
    public Task The_event_panel_is_laid_out_by_the_mark_an_open_panel_carries() => RunAsync(async () =>
    {
        var scope = await OpenAsync();
        var trigger = Trigger(scope, "Team sync");
        var panel = Panel(trigger);

        await trigger.ClickAsync();

        // `data-open:flex` at the call site: the panel says it is open once the page has heard so.
        await Expect(panel).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(panel).ToHaveCSSAsync("display", "flex");
        await Expect(panel).ToHaveCSSAsync("padding-top", "0px");
        var button = (await trigger.BoundingBoxAsync())!;
        var box = (await panel.BoundingBoxAsync())!;
        Assert.Equal(320, box.Width, 1.0);
        Assert.Equal(button.X + (button.Width / 2), box.X + (box.Width / 2), 1.0);
    });

    [Fact]
    public Task On_a_phone_a_wide_panel_is_kept_inside_the_page_and_one_at_the_foot_opens_over_its_trigger() => RunAsync(async () =>
    {
        var scope = await OpenAsync();
        await Page.SetViewportSizeAsync(390, 844);
        var options = Trigger(scope, "Options");

        var wide = await OpenedAsync(scope, "Feedback");
        await options.EvaluateAsync("el => window.scrollBy(0, el.getBoundingClientRect().bottom - innerHeight + 8)");
        var low = await OpenedAsync(scope, "Options");

        // Nearly as wide as the page: the anchor rules alone left it wherever its trigger was, past the right
        // edge. Flux keeps such a panel 5px inside both.
        Assert.True(wide.Panel.Width > 300, $"the panel is {wide.Panel.Width}px wide: any place would hold it");
        Assert.InRange(wide.Panel.X, 4, 390);
        Assert.InRange(wide.Panel.X + wide.Panel.Width, 0, 386);
        // No room under a trigger 8px from the foot of the viewport: over it, 5px apart.
        Assert.Equal(low.Trigger.Y - 5, low.Panel.Y + low.Panel.Height, 1.0);
        Assert.True(low.Panel.Y >= 0, "the panel opened past the top of the viewport");
    });

    private static ILocator Trigger(ILocator scope, string name) =>
        scope.Locator("[data-ui-dropdown] > button").Filter(new LocatorFilterOptions { HasText = name });

    // The panel comes right after its trigger, inside the dropdown that holds them both.
    private static ILocator Panel(ILocator trigger) => trigger.Locator("xpath=following-sibling::*[@data-ui-popover]");

    // Opens one dropdown, measures the trigger and its panel, and closes it again.
    private async Task<(LocatorBoundingBoxResult Trigger, LocatorBoundingBoxResult Panel)> OpenedAsync(ILocator scope, string name)
    {
        var trigger = Trigger(scope, name);
        var panel = Panel(trigger);
        await trigger.ScrollIntoViewIfNeededAsync();
        await trigger.ClickAsync();
        await Expect(panel).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        var boxes = ((await trigger.BoundingBoxAsync())!, (await panel.BoundingBoxAsync())!);
        await Page.Keyboard.PressAsync("Escape");
        await Expect(panel).ToBeHiddenAsync();

        return boxes;
    }

    // The popover page: every example on it is a dropdown around a trigger, so the page is the scope.
    private async Task<ILocator> OpenAsync()
    {
        await OpenUiPageAsync("popover", "Popover");

        return Page.Locator("main");
    }
}
