using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     The kit's Actions components, in a browser.
/// </summary>
/// <remarks>
///     <para>
///         The unit tests assert the markup: which daisyUI classes a set of props writes. They cannot
///         assert the two things that actually break. The first is that a class STYLES anything —
///         <c>ToHtml()</c> renders exactly the class the call site asked for whether or not daisyUI
///         emitted a rule for it, so an unstyled component passes every markup assertion and appears as
///         a zero-sized box only in a browser. The second is that a callback runs at all: a handler is
///         not in the static HTML, it is attached when the runtime boots.
///     </para>
///     <para>
///         So every check here is one of those two — visible with a real size, or press it and watch the
///         state change.
///     </para>
/// </remarks>
[Collection(WasmExampleCollection.Name)]
public sealed class UiKitActionsTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Wasm";
    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task Every_action_component_renders_with_a_real_size() => RunAsync(async () =>
    {
        await OpenAsync();

        // The FAB is absent here on purpose: it is `position: fixed`, so its wrapper in the flow has no
        // height of its own and a bounding box on the wrapper would measure nothing. It is measured
        // below, on the floating element itself.
        foreach (var id in new[]
                 {
                     "ui-button", "ui-dropdown", "ui-context-menu", "ui-command", "ui-modal", "ui-modal-confirm",
                     "ui-modal-flyout", "ui-modal-floating", "ui-modal-options", "ui-modal-state", "ui-swap",
                     "ui-theme-controller",

                 })
        {
            var node = Page.Locator($"[data-testid='{id}']");
            await Expect(node).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

            // A component whose classes reached no stylesheet still renders, and still passes every
            // markup assertion — it is simply a box of zero height. This is the check that catches it.
            var box = await node.BoundingBoxAsync();
            Assert.NotNull(box);
            Assert.True(box!.Height > 0, $"{id} rendered with zero height, which is what an unstyled kit component looks like.");
            Assert.True(box.Width > 0, $"{id} rendered with zero width.");
        }

        var fab = Page.Locator("[data-testid='ui-fab'] .fab");
        await Expect(fab).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        var fabBox = await fab.BoundingBoxAsync();
        Assert.NotNull(fabBox);
        Assert.True(fabBox!.Height > 0, "the FAB rendered with zero height.");
    });

    [Fact]
    public Task The_dropdown_opens_and_closes_from_CSharp_state() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-dropdown']");
        var trigger = scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Open menu" });
        await Expect(trigger).ToHaveAttributeAsync("aria-expanded", "false", new LocatorAssertionsToHaveAttributeOptions { Timeout = 15_000 });

        await trigger.ClickAsync();

        // The popover opened, C# heard it through the toggle event, and the page's own state now drives the label.
        var panel = scope.Locator("[popover]").First;
        await Expect(panel).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        var reopened = scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Close menu" });
        await Expect(reopened).ToHaveAttributeAsync("aria-expanded", "true", new LocatorAssertionsToHaveAttributeOptions { Timeout = 10_000 });

        // The menu took focus when it opened, so the keyboard works straight away.
        await Expect(panel.Locator("[role='menu']").First).ToBeFocusedAsync();

        await Page.Keyboard.PressAsync("Escape");
        await Expect(panel).ToBeHiddenAsync();
        await Expect(scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Open menu" }))
            .ToBeFocusedAsync(new LocatorAssertionsToBeFocusedOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task Choosing_a_menu_action_closes_the_dropdown_and_reports_it() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-dropdown']");

        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Open menu" }).ClickAsync();
        await scope.GetByRole(AriaRole.Menuitem, new LocatorGetByRoleOptions { Name = "Duplicate" }).ClickAsync();

        // Both halves matter: the action ran, and the menu closed itself afterwards.
        await Expect(Page.Locator("[data-testid='ui-actions-log']")).ToContainTextAsync("duplicate");
        await Expect(scope.Locator("[popover]").First).ToBeHiddenAsync();
    });

    [Fact]
    public Task The_menu_is_driven_by_the_keyboard_into_a_submenu() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-dropdown']");
        var trigger = scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "View" });

        await trigger.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");

        var menu = scope.Locator("[role='menu'][autofocus]").Last;
        await Expect(menu).ToBeFocusedAsync(new LocatorAssertionsToBeFocusedOptions { Timeout = 10_000 });

        async Task<string> CursorAsync() =>
            await menu.EvaluateAsync<string>(
                "m => { const r = document.getElementById(m.getAttribute('aria-activedescendant') || ''); "
                + "return r ? r.textContent.trim() : ''; }");

        // Home puts the cursor on the first row, Right walks into "Sort by", and Down moves among its options.
        await Page.Keyboard.PressAsync("Home");
        await WaitForCursorAsync(CursorAsync, "Sort by");
        await Page.Keyboard.PressAsync("ArrowRight");
        await WaitForCursorAsync(CursorAsync, "Name");
        await Expect(scope.Locator(".ui-menu-flyout").First).ToBeVisibleAsync();

        await Page.Keyboard.PressAsync("ArrowDown");
        await WaitForCursorAsync(CursorAsync, "Date modified");

        // Enter presses the row under the cursor, exactly as a click would.
        await Page.Keyboard.PressAsync("Enter");
        await Expect(Page.Locator("[data-testid='ui-actions-log']")).ToContainTextAsync("sorted by date");
    });

    [Fact]
    public Task A_pointer_crossing_diagonally_into_a_submenu_keeps_it_open() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-dropdown']");

        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "View" }).ClickAsync();

        var sub = scope.GetByRole(AriaRole.Menuitem, new LocatorGetByRoleOptions { Name = "Sort by" });
        await sub.HoverAsync(new LocatorHoverOptions { Timeout = 10_000 });
        var flyout = scope.Locator(".ui-menu-flyout").First;
        await Expect(flyout).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // From the middle of the row to the flyout's last option, in small steps: the path crosses the row below
        // ("Refresh"), which without the safe triangle would take the hover and close the flyout on the way.
        var from = (await sub.BoundingBoxAsync())!;
        var target = flyout.GetByRole(AriaRole.Menuitemradio, new LocatorGetByRoleOptions { Name = "Size" });
        var to = (await target.BoundingBoxAsync())!;
        await Page.Mouse.MoveAsync(from.X + (from.Width * 0.6f), from.Y + (from.Height / 2));
        await Page.Mouse.MoveAsync(to.X + 12, to.Y + (to.Height / 2), new MouseMoveOptions { Steps = 25 });

        await Expect(flyout).ToBeVisibleAsync();

        await target.ClickAsync();
        await Expect(Page.Locator("[data-testid='ui-actions-log']")).ToContainTextAsync("sorted by size");
    });

    [Fact]
    public Task A_checkbox_item_toggles_and_keeps_the_menu_open() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-dropdown']");

        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "View" }).ClickAsync();
        var item = scope.GetByRole(AriaRole.Menuitemcheckbox, new LocatorGetByRoleOptions { Name = "Show archived" });

        await item.ClickAsync();
        await Expect(item).ToHaveAttributeAsync("aria-checked", "true", new LocatorAssertionsToHaveAttributeOptions { Timeout = 10_000 });
        // A menu of switches stays up: flipping three should not mean opening it three times.
        await Page.WaitForTimeoutAsync(300);
        await Expect(item).ToBeVisibleAsync();
    });

    private static async Task WaitForCursorAsync(Func<Task<string>> read, string expected)
    {
        var last = "";
        for (var i = 0; i < 50; i++)
        {
            last = await read();
            if (last.StartsWith(expected, StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(100);
        }

        Assert.Fail($"the menu cursor never reached \"{expected}\" (it is on \"{last}\").");
    }

    [Fact]
    public Task A_right_click_opens_the_menu_at_the_pointer_with_the_menu_keyboard() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-context-menu']");
        var card = scope.GetByText("Right-click this card");
        await card.ScrollIntoViewIfNeededAsync();
        var box = (await card.BoundingBoxAsync())!;
        var x = box.X + 24;
        var y = box.Y + 8;
        var panel = scope.Locator("[popover]");

        // The hook is installed when the runtime loads; a right-click that lands on the prerendered page first gets
        // the browser's own menu, so press again until the runtime is there to replace it.
        for (var attempt = 0; attempt < 10 && !await panel.IsVisibleAsync(); attempt++)
        {
            await Page.Mouse.ClickAsync(x, y, new MouseClickOptions { Button = MouseButton.Right });
            await Page.WaitForTimeoutAsync(300);
        }

        await Expect(panel).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // Still open once the button is released — the press opens it on macOS and Linux, and the release that
        // follows would light-dismiss a menu shown before it.
        await Page.WaitForTimeoutAsync(300);
        await Expect(panel).ToBeVisibleAsync();

        // At the pointer, not centred on the screen the way an unplaced popover would be.
        var at = (await panel.BoundingBoxAsync())!;
        Assert.InRange(at.X, x - 2, x + 2);
        Assert.InRange(at.Y, y - 2, y + 2);

        // The same keyboard a dropdown has: focus is on the menu, the arrows move the cursor, Enter presses the row.
        var menu = scope.Locator("[role='menu']");
        await Expect(menu).ToBeFocusedAsync(new LocatorAssertionsToBeFocusedOptions { Timeout = 10_000 });

        async Task<string> CursorAsync() =>
            await menu.EvaluateAsync<string>(
                "m => { const r = document.getElementById(m.getAttribute('aria-activedescendant') || ''); "
                + "return r ? r.textContent.trim() : ''; }");

        await WaitForCursorAsync(CursorAsync, "Open");
        await Page.Keyboard.PressAsync("ArrowDown");
        await WaitForCursorAsync(CursorAsync, "Copy link");
        await Page.Keyboard.PressAsync("Enter");

        await Expect(Page.Locator("[data-testid='ui-actions-log']")).ToContainTextAsync("copied the link");
        await Expect(panel).ToBeHiddenAsync();
    });

    [Fact]
    public Task A_context_menu_opened_at_the_edge_of_the_viewport_stays_on_screen() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-context-menu']");
        var card = scope.GetByText("Right-click this card");
        await card.ScrollIntoViewIfNeededAsync();
        var panel = scope.Locator("[popover]");

        // A real MouseEvent on the card, reporting a pointer at the bottom-right corner of the viewport — where a menu
        // placed at the pointer would open off both edges. The card is not under that corner, so a real mouse cannot
        // be sent there; the event is what the runtime reads either way.
        var size = Page.ViewportSize!;
        for (var attempt = 0; attempt < 10 && !await panel.IsVisibleAsync(); attempt++)
        {
            await card.EvaluateAsync(
                "(el, p) => el.dispatchEvent(new MouseEvent('contextmenu', "
                + "{ bubbles: true, cancelable: true, button: 2, clientX: p[0], clientY: p[1] }))",
                new[] { size.Width - 2, size.Height - 2 });
            await Page.WaitForTimeoutAsync(300);
        }

        await Expect(panel).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        var at = (await panel.BoundingBoxAsync())!;
        Assert.True(at.X + at.Width <= size.Width, $"the menu ran off the right edge ({at.X}+{at.Width}).");
        Assert.True(at.Y + at.Height <= size.Height, $"the menu ran off the bottom edge ({at.Y}+{at.Height}).");
        // Pulled back only as far as it had to be: still in the corner the pointer was in, not reset to the origin.
        Assert.True(at.X > size.Width / 2 && at.Y > size.Height / 2, $"the menu left the pointer's corner ({at.X},{at.Y}).");

        await Page.Keyboard.PressAsync("Escape");
        await Expect(panel).ToBeHiddenAsync();
    });

    [Fact]
    public Task The_shortcut_opens_the_palette_and_Enter_runs_the_narrowed_command() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-command']");
        var dialog = scope.Locator("dialog");
        await Expect(scope.Locator("[data-rask-shortcut]")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // The hook is installed when the runtime loads, so press until it is there to answer.
        for (var attempt = 0; attempt < 10 && !await dialog.IsVisibleAsync(); attempt++)
        {
            await Page.Keyboard.PressAsync("ControlOrMeta+k");
            await Page.WaitForTimeoutAsync(300);
        }

        await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        var box = dialog.GetByRole(AriaRole.Combobox);
        await Expect(box).ToBeFocusedAsync(new LocatorAssertionsToBeFocusedOptions { Timeout = 10_000 });

        await box.PressSequentiallyAsync("copy");
        await Expect(dialog.GetByRole(AriaRole.Option)).ToHaveCountAsync(1,
            new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });
        await Expect(box).ToHaveAttributeAsync("aria-activedescendant",
            (await dialog.GetByRole(AriaRole.Option).GetAttributeAsync("id"))!);

        await Page.Keyboard.PressAsync("Enter");

        await Expect(Page.Locator("[data-testid='ui-actions-log']")).ToContainTextAsync("copied the invoice link",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        await Expect(dialog).ToBeHiddenAsync();
    });

    [Fact]
    public Task The_palette_field_opens_it_and_the_arrows_skip_a_disabled_command() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-command']");
        var field = scope.Locator("[data-rask-shortcut]");

        await field.ScrollIntoViewIfNeededAsync();
        await field.ClickAsync();

        var dialog = scope.Locator("dialog");
        await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        var box = dialog.GetByRole(AriaRole.Combobox);
        await Expect(box).ToBeFocusedAsync(new LocatorAssertionsToBeFocusedOptions { Timeout = 10_000 });

        async Task<string> HighlightedAsync() =>
            await box.EvaluateAsync<string>(
                "b => { const r = document.getElementById(b.getAttribute('aria-activedescendant') || ''); "
                + "return r ? r.textContent.trim() : ''; }");

        await Page.Keyboard.PressAsync("ArrowDown");
        // "Export all" is disabled, so one step from "New invoice" lands past it.
        await WaitForCursorAsync(HighlightedAsync, "Copy invoice link");

        await Page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToBeHiddenAsync();
    });

    [Fact]
    public Task The_modal_opens_from_its_trigger_as_a_real_modal_and_escape_closes_it() => RunAsync(async () =>
    {
        await OpenAsync();

        var trigger = Page.Locator("[data-testid='ui-modal']")
            .GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Edit profile" });
        var dialog = Page.Locator("#edit-profile");
        var log = Page.Locator("[data-testid='ui-actions-log']");
        await Expect(dialog).ToBeHiddenAsync();

        await trigger.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // The invoker command, not the popover fallback: a :modal <dialog>, the page behind inert and locked.
        Assert.Equal("DIALOG", await dialog.EvaluateAsync<string>("el => el.tagName"));
        Assert.True(await dialog.EvaluateAsync<bool>("d => d.matches(':modal')"), "the kit's dialog opened as a popover");
        Assert.Equal("hidden", await Page.EvaluateAsync<string>("() => getComputedStyle(document.documentElement).overflowY"));
        // Nothing is ringed when it opens, as on Flux: the placeholder took the focus and left.
        Assert.False(
            await dialog.EvaluateAsync<bool>("d => d.contains(document.activeElement) && document.activeElement.matches('input, button')"),
            "a control was focused as the modal opened.");

        // A <details> toggling INSIDE the dialog is not the dialog closing: toggle does not bubble, but the
        // runtime's capture-phase delegation used to hand it to the nearest element with a toggle handler —
        // the dialog, whose OnClose reads a closed state from it (#1116 review).
        await Page.Locator("[data-testid='ui-modal-more'] summary").ClickAsync();
        await Expect(Page.Locator("[data-testid='ui-modal-more']")).ToHaveAttributeAsync("open", "");
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(log).Not.ToContainTextAsync("closed the profile");

        await Page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(log).ToContainTextAsync("closed the profile", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        Assert.NotEqual("hidden", await Page.EvaluateAsync<string>("() => getComputedStyle(document.documentElement).overflowY"));
        await Expect(trigger).ToBeFocusedAsync();
    });

    [Fact]
    public Task A_click_outside_closes_the_modal_and_a_click_on_its_own_padding_does_not() => RunAsync(async () =>
    {
        await OpenAsync();

        var dialog = Page.Locator("#edit-profile");
        await Page.Locator("[data-testid='ui-modal']")
            .GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Edit profile" }).ClickAsync();
        await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        var box = (await dialog.BoundingBoxAsync())!;

        await Page.Mouse.ClickAsync(box.X + 6, box.Y + (box.Height / 2));
        await Expect(dialog).ToBeVisibleAsync();

        await Page.Mouse.ClickAsync(4, 4);
        await Expect(dialog).ToBeHiddenAsync();
    });

    [Fact]
    public Task The_modal_is_drawn_as_Flux_draws_it() => RunAsync(async () =>
    {
        await OpenAsync();

        var dialog = Page.Locator("#edit-profile");
        await Page.Locator("[data-testid='ui-modal']")
            .GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Edit profile" }).ClickAsync();
        await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // A class that reached no stylesheet still renders. These are the ones that make it a panel.
        Assert.Equal("24px", await dialog.EvaluateAsync<string>("d => getComputedStyle(d).paddingTop"));
        Assert.Equal("12px", await dialog.EvaluateAsync<string>("d => getComputedStyle(d).borderTopLeftRadius"));
        Assert.Equal("384px", await dialog.EvaluateAsync<string>("d => getComputedStyle(d).width"));
        Assert.Equal("rgba(0, 0, 0, 0.1)", await dialog.EvaluateAsync<string>("d => getComputedStyle(d, '::backdrop').backgroundColor"));
        var close = dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Close modal" });
        var corner = (await close.BoundingBoxAsync())!;
        var panel = (await dialog.BoundingBoxAsync())!;
        Assert.Equal(16, Math.Round(panel.X + panel.Width - corner.X - corner.Width));
        Assert.Equal(16, Math.Round(corner.Y - panel.Y));

        await close.ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();
    });

    [Fact]
    public Task The_confirmation_closes_from_its_cancel_and_its_delete_still_runs_its_handler() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-modal-confirm']");
        var dialog = Page.Locator("#delete-profile");
        var open = scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete", Exact = true });

        await open.ClickAsync();
        await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Cancel" }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();

        // Ui.ModalClose closes it by command, and the button's own handler runs all the same.
        await open.ClickAsync();
        await Expect(dialog).ToBeVisibleAsync();
        await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete project" }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(Page.Locator("[data-testid='ui-actions-log']"))
            .ToContainTextAsync("deleted the project", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    [Theory]
    [InlineData("Edit profile", "edit-profile-flyout", "right")]
    [InlineData("From the left", "flyout-left", "left")]
    [InlineData("From the bottom", "flyout-bottom", "bottom")]
    public Task A_flyout_sits_against_the_edge_it_opens_from(string button, string name, string edge) => RunAsync(async () =>
    {
        await OpenAsync();

        await Page.Locator("[data-testid='ui-modal-flyout']")
            .GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = button }).ClickAsync();
        var dialog = Page.Locator("#" + name);
        await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        // Past the 150ms it slides for.
        await Page.WaitForTimeoutAsync(300);

        var viewport = await Page.EvaluateAsync<double[]>("() => [document.documentElement.clientWidth, innerHeight]");
        var rect = (await dialog.BoundingBoxAsync())!;
        if (string.Equals(edge, "bottom", StringComparison.Ordinal))
        {
            Assert.True(rect.Width >= viewport[0] - 2, $"the bottom flyout is {rect.Width}px wide in a {viewport[0]}px viewport.");
            Assert.True(rect.Y + rect.Height >= viewport[1] - 2, "the bottom flyout is not against the bottom edge.");
        }
        else
        {
            Assert.True(rect.Height >= viewport[1] - 2, $"the flyout is {rect.Height}px tall in a {viewport[1]}px viewport.");
            Assert.True(
                string.Equals(edge, "left", StringComparison.Ordinal) ? rect.X <= 2 : rect.X + rect.Width >= viewport[0] - 2,
                $"the flyout is not against the {edge} edge.");
        }

        await Page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToBeHiddenAsync();
    });

    [Fact]
    public Task The_floating_flyout_stands_off_the_edges_of_the_viewport() => RunAsync(async () =>
    {
        await OpenAsync();

        await Page.Locator("[data-testid='ui-modal-floating']")
            .GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Edit profile" }).ClickAsync();
        var dialog = Page.Locator("#edit-profile-floating");
        await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Page.WaitForTimeoutAsync(300);

        var viewport = await Page.EvaluateAsync<double[]>("() => [document.documentElement.clientWidth, innerHeight]");
        var rect = (await dialog.BoundingBoxAsync())!;
        Assert.Equal(8, Math.Round(rect.Y));
        Assert.Equal(8, Math.Round(viewport[0] - rect.X - rect.Width));
        Assert.Equal(8, Math.Round(viewport[1] - rect.Y - rect.Height));
        Assert.Equal("12px", await dialog.EvaluateAsync<string>("d => getComputedStyle(d).borderTopLeftRadius"));

        await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Cancel" }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();
    });

    [Fact]
    public Task A_modal_that_is_not_dismissible_ignores_a_click_outside() => RunAsync(async () =>
    {
        await OpenAsync();

        await Page.Locator("[data-testid='ui-modal-options']")
            .GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Filters" }).ClickAsync();
        var dialog = Page.Locator("#demo-filters");
        await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        await Page.Mouse.ClickAsync(4, 4);
        await Page.WaitForTimeoutAsync(300);
        await Expect(dialog).ToBeVisibleAsync();

        await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Apply" }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();
    });

    [Fact]
    public Task A_long_modal_set_to_scroll_the_body_runs_past_the_bottom_of_the_screen() => RunAsync(async () =>
    {
        await OpenAsync();

        await Page.Locator("[data-testid='ui-modal-options']")
            .GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Terms" }).ClickAsync();
        var dialog = Page.Locator("#demo-terms");
        await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // The layer is the scroller: taller inside than the viewport it fills.
        Assert.True(
            await dialog.EvaluateAsync<bool>("d => d.scrollHeight > d.clientHeight && d.clientHeight >= innerHeight - 1"),
            "the modal did not extend past the viewport.");

        // The room around the panel is outside it, so a click there closes it.
        await Page.Mouse.ClickAsync(4, 4);
        await Expect(dialog).ToBeHiddenAsync();
    });

    [Fact]
    public Task The_state_driven_modal_traps_focus_and_tells_a_dismissal_from_a_close() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-modal-state']");
        var opener = scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete order" }).First;
        var dialog = scope.Locator("dialog");
        var log = Page.Locator("[data-testid='ui-actions-log']");
        await Expect(dialog).ToHaveCountAsync(0);

        await opener.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(dialog).ToContainTextAsync("This cannot be undone.");

        // The runtime's trap holds focus; Tab cycles inside rather than reaching the page behind.
        await Expect(dialog).ToHaveAttributeAsync("data-rask-focus-trap", "");
        for (var i = 0; i < 5; i++)
        {
            await Page.Keyboard.PressAsync("Tab");
            Assert.True(
                await dialog.EvaluateAsync<bool>("d => d.contains(document.activeElement)"),
                "Tab escaped the state-driven dialog.");
        }

        // Escape presses the dismiss control: OnCancel hears it, OnClose stops rendering it, focus goes back.
        await Page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        await Expect(opener).ToBeFocusedAsync();
        await Expect(log).ToContainTextAsync("dismissed the dialog", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Cancel is a close, not a dismissal — and Delete, which has a handler of its own, is neither.
        await opener.ClickAsync();
        await Expect(dialog).ToBeVisibleAsync();
        await dialog.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete", Exact = true }).ClickAsync();
        await Expect(dialog).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        await Expect(log).ToContainTextAsync("deleted the order", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task The_swap_flips_its_face_and_says_so() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-swap']");
        var swap = scope.Locator("button.swap");

        await Expect(swap).ToHaveAttributeAsync("aria-pressed", "false");
        await Expect(scope).ToContainTextAsync("Playing");

        await swap.ClickAsync();

        // The class is what daisyUI draws from, and aria-pressed is what a screen reader reads. A swap
        // that changed one without the other would look right and announce nothing.
        await Expect(swap).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("swap-active"));
        await Expect(swap).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(scope).ToContainTextAsync("Muted");
    });

    [Fact]
    public Task The_floating_action_button_reveals_its_actions_on_focus() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-fab']");
        var photo = scope.GetByText("Photo");

        // Hidden by `visibility`, so it occupies space and is present in the DOM — ToBeVisibleAsync is
        // what distinguishes the two states, not a count.
        await Expect(photo).ToBeHiddenAsync();

        await scope.Locator("[tabindex='0']").First.FocusAsync();
        await Expect(photo).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task The_theme_control_chooses_a_theme_and_the_page_applies_it() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-theme-controller']");
        var box = Page.Locator("[data-testid='ui-theme-scope']");

        await Expect(box).ToHaveAttributeAsync("data-theme", "light");

        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Retro" }).ClickAsync();

        // The control reports; the PAGE writes data-theme onto an ancestor of the things to repaint.
        // That split is not an inconvenience of the design, it is the only shape available: no component
        // can write an attribute onto something above it.
        await Expect(box).ToHaveAttributeAsync("data-theme", "retro");
        await Expect(box).ToContainTextAsync("retro theme");

        // And it really repaints: a theme that changed the attribute and no pixels would be a scope
        // that daisyUI never matched.
        var painted = await box.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Dark" }).ClickAsync();
        await Expect(box).ToHaveAttributeAsync("data-theme", "dark");
        var repainted = await box.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        Assert.NotEqual(painted, repainted);
    });

    [Fact]
    public Task An_icon_only_button_still_has_a_name() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-button']");

        // A circle holds one glyph, so its label cannot be visible text. Reaching it by accessible name
        // is the proof it is still announced rather than read out as "button".
        await Expect(scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Close" }))
            .ToBeVisibleAsync();
        await Expect(scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Add" }))
            .ToBeVisibleAsync();
    });

    [Fact]
    public Task A_disabled_button_is_disabled_by_the_browser_rather_than_by_a_class() => RunAsync(async () =>
    {
        await OpenAsync();

        // daisyUI's btn-disabled styles without disabling: a button carrying only that class still takes
        // the click and still reaches its handler.
        var disabled = Page.Locator("[data-testid='ui-button']")
            .GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Disabled" });

        await Expect(disabled).ToBeDisabledAsync();
    });

    [Fact]
    public Task A_button_waiting_on_its_handler_shows_a_spinner_keeps_its_width_and_takes_one_press() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-button-loading']");
        var save = scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Save" });
        var count = Page.Locator("[data-testid='ui-button-loading-count']");
        await Expect(count).ToContainTextAsync("Saved 0 times", new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });

        var before = await save.BoundingBoxAsync();

        await save.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");

        // The WASM host ends the mark when the dispatch promise resolves, so it is up for the 1.5 s the handler
        // takes — and the stylesheet draws it: the label goes transparent, the width holds, the spinner sits on top.
        await Expect(save).ToHaveAttributeAsync("aria-busy", "true", new LocatorAssertionsToHaveAttributeOptions { Timeout = 5_000 });
        // daisyUI transitions a button's colour, so the label fades rather than vanishing: wait for the fade to end,
        // whatever colour space the engine reports it in.
        await Page.WaitForFunctionAsync(
            "el => { const c = getComputedStyle(el).color; return /rgba\\(.*,\\s*0\\)$/.test(c) || /\\/\\s*0\\)$/.test(c) || c === 'transparent'; }",
            await save.ElementHandleAsync(),
            new PageWaitForFunctionOptions { Timeout = 5_000 });
        Assert.NotEqual("none", await save.EvaluateAsync<string>("el => getComputedStyle(el, '::after').maskImage"));
        var during = await save.BoundingBoxAsync();
        Assert.Equal(before!.Width, during!.Width, 0.5);

        await Page.Keyboard.PressAsync("Enter");

        await Expect(count).ToContainTextAsync("Saved 1 time", new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        await Expect(save).Not.ToHaveAttributeAsync("aria-busy", "true", new LocatorAssertionsToHaveAttributeOptions { Timeout = 5_000 });
        await Page.WaitForTimeoutAsync(2_000);
        await Expect(count).ToContainTextAsync("Saved 1 time ");
    });

    [Fact]
    public Task A_button_given_a_route_navigates_without_reloading_the_app() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-button-route']");
        var button = scope.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Navigation components" });
        var github = scope.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "GitHub" });

        // The markup half: the route is intercepted, the external new-tab link is not.
        await Expect(button).ToHaveAttributeAsync("data-rask-nav", "");
        await Expect(github).ToHaveAttributeAsync("target", "_blank");
        Assert.Null(await github.GetAttributeAsync("data-rask-nav"));

        // The behaviour half. A value on window survives only a client-side navigation: a full document
        // load starts a fresh window and boots the app again, which is what a link without data-rask-nav
        // costs even when its URL is the same.
        await Page.EvaluateAsync("() => { window.__raskStayedInApp = true; }");
        await button.ClickAsync();

        await Expect(Page.Locator("main h1")).ToContainTextAsync("Navigation",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
        Assert.True(await Page.EvaluateAsync<bool>("() => window.__raskStayedInApp === true"),
            "the button reloaded the app instead of navigating inside it.");
    });

    private async Task OpenAsync()
    {
        await Page.GotoAsync(Docs);
        await Expect(Page.Locator(".side-nav a.side-nav-link[aria-current='page']").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await ClickSidebar("Actions");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Actions",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
    }
}
