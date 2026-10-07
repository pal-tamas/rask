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
                     "ui-button", "ui-button-variants", "ui-button-groups", "ui-dropdown", "ui-context-menu", "ui-command", "ui-modal", "ui-modal-popover", "ui-swap",
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
        var menu = scope.Locator("[data-ui-menu]:popover-open");
        await Expect(menu).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        var reopened = scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Close menu", IncludeHidden = true });
        await Expect(reopened).ToHaveAttributeAsync("aria-expanded", "true", new LocatorAssertionsToHaveAttributeOptions { Timeout = 10_000 });

        // The menu took focus when it opened, with no row chosen: the keyboard works straight away.
        await Expect(menu).ToBeFocusedAsync();
        await Expect(menu.Locator("[data-active]")).ToHaveCountAsync(0);

        await Page.Keyboard.PressAsync("Escape");
        await Expect(scope.Locator("[data-ui-menu]:popover-open")).ToHaveCountAsync(0);
        await Expect(scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Open menu" }))
            .ToBeFocusedAsync(new LocatorAssertionsToBeFocusedOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task Choosing_a_menu_action_closes_the_dropdown_and_reports_it() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-dropdown']");

        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Shortcuts" }).ClickAsync();
        await scope.GetByRole(AriaRole.Menuitem, new LocatorGetByRoleOptions { Name = "Duplicate" }).ClickAsync();

        // Both halves matter: the action ran, and the menu closed itself afterwards.
        await Expect(Page.Locator("[data-testid='ui-actions-log']")).ToContainTextAsync("duplicate");
        await Expect(scope.Locator("[data-ui-menu]:popover-open")).ToHaveCountAsync(0);
    });

    [Fact]
    public Task The_keyboard_walks_the_menu_as_Flux_does_with_focus_on_the_row_under_the_cursor() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-dropdown']");
        var trigger = scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Options" });

        await trigger.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");

        var menu = scope.Locator("[data-ui-menu]:popover-open");
        await Expect(menu).ToBeFocusedAsync(new LocatorAssertionsToBeFocusedOptions { Timeout = 10_000 });

        // Down starts at the top; real focus follows the cursor, row by row, and `data-active` marks it.
        await Page.Keyboard.PressAsync("ArrowDown");
        await WaitForCursorAsync(FocusedRowAsync, "New post");
        await Page.Keyboard.PressAsync("ArrowUp");
        // No wrapping: Up at the first row stays there, and Home and End are not menu keys.
        await Page.Keyboard.PressAsync("End");
        await WaitForCursorAsync(FocusedRowAsync, "New post");
        await Page.Keyboard.PressAsync("ArrowDown");
        await WaitForCursorAsync(FocusedRowAsync, "Sort by");

        // Right walks into the submenu, onto its first row.
        await Page.Keyboard.PressAsync("ArrowRight");
        await WaitForCursorAsync(FocusedRowAsync, "Name");
        await Expect(scope.Locator("[data-ui-menu-submenu] > [data-ui-menu]").First).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("ArrowDown");
        await WaitForCursorAsync(FocusedRowAsync, "Date");

        // Left closes it and puts the cursor back on its row; Enter opens it again.
        await Page.Keyboard.PressAsync("ArrowLeft");
        await WaitForCursorAsync(FocusedRowAsync, "Sort by");
        await Page.Keyboard.PressAsync("Enter");
        await WaitForCursorAsync(FocusedRowAsync, "Name");
        await Page.Keyboard.PressAsync("ArrowDown");
        await WaitForCursorAsync(FocusedRowAsync, "Date");

        // Enter presses the row that has focus, exactly as a click would, and the whole menu closes.
        await Page.Keyboard.PressAsync("Enter");
        await Expect(Page.Locator("[data-testid='ui-actions-log']")).ToContainTextAsync("sorted by date");
        await Expect(scope.Locator("[data-ui-menu]:popover-open")).ToHaveCountAsync(0);
        await Expect(trigger).ToBeFocusedAsync(new LocatorAssertionsToBeFocusedOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task ArrowDown_on_the_trigger_opens_the_menu_onto_its_first_row_and_a_letter_jumps() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-dropdown']");
        var trigger = scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Groups" });

        await trigger.FocusAsync();
        await Page.Keyboard.PressAsync("ArrowDown");

        await Expect(scope.Locator("[data-ui-menu]:popover-open")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await WaitForCursorAsync(FocusedRowAsync, "View");
        await Page.Keyboard.PressAsync("s");
        await WaitForCursorAsync(FocusedRowAsync, "Share");

        // Tab leaves the menu, which closes it.
        await Page.Keyboard.PressAsync("Tab");
        await Expect(scope.Locator("[data-ui-menu]:popover-open")).ToHaveCountAsync(0);
    });

    [Fact]
    public Task A_pointer_crossing_diagonally_into_a_submenu_keeps_it_open() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-dropdown']");

        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Options" }).ClickAsync();

        var sub = scope.GetByRole(AriaRole.Menuitem, new LocatorGetByRoleOptions { Name = "Sort by" });
        await sub.HoverAsync(new LocatorHoverOptions { Timeout = 10_000 });
        var flyout = scope.Locator("[data-ui-menu-submenu] > [data-ui-menu]").First;
        await Expect(flyout).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // Beside its row, overlapping the menu's padding by five pixels, as Flux places it.
        var from = (await sub.BoundingBoxAsync())!;
        var beside = (await flyout.BoundingBoxAsync())!;
        Assert.InRange(beside.X, from.X + from.Width - 6, from.X + from.Width - 4);
        Assert.InRange(beside.Y, from.Y - 1, from.Y + 1);

        // From near the end of the row to the flyout's last option, in small steps: the path crosses the row
        // below ("Filter"), which without the safe triangle would take the hover and swap the flyout on the way.
        var target = flyout.GetByRole(AriaRole.Menuitemradio, new LocatorGetByRoleOptions { Name = "Popularity" });
        var to = (await target.BoundingBoxAsync())!;
        await Page.Mouse.MoveAsync(from.X + (from.Width * 0.8f), from.Y + (from.Height / 2));
        await Page.Mouse.MoveAsync(to.X + 12, to.Y + (to.Height / 2), new MouseMoveOptions { Steps = 25 });

        await Expect(flyout).ToBeVisibleAsync();

        await target.ClickAsync();
        await Expect(Page.Locator("[data-testid='ui-actions-log']")).ToContainTextAsync("sorted by popularity");
    });

    [Fact]
    public Task A_checkbox_row_closes_the_menu_unless_the_menu_or_the_row_keeps_it_open() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-dropdown']");
        var open = scope.Locator("[data-ui-menu]:popover-open");

        // A pick closes the menu, checkbox or not — Flux's default.
        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Permissions" }).ClickAsync();
        await open.GetByRole(AriaRole.Menuitemcheckbox, new LocatorGetByRoleOptions { Name = "Delete" }).ClickAsync();
        await Expect(open).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });

        // KeepOpen on the menu: ticking three should not mean opening it three times.
        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Keep open" }).ClickAsync();
        var archived = open.GetByRole(AriaRole.Menuitemcheckbox, new LocatorGetByRoleOptions { Name = "Archived" });
        await archived.ClickAsync();
        await Expect(archived).ToHaveAttributeAsync("aria-checked", "true", new LocatorAssertionsToHaveAttributeOptions { Timeout = 10_000 });
        await Page.WaitForTimeoutAsync(300);
        await Expect(archived).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Escape");

        // KeepOpen on the rows only: the checkboxes stay, and "Clear" closes it.
        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Filters" }).ClickAsync();
        await open.GetByRole(AriaRole.Menuitemcheckbox, new LocatorGetByRoleOptions { Name = "Draft" }).ClickAsync();
        await Page.WaitForTimeoutAsync(300);
        await Expect(open).ToBeVisibleAsync();
        await open.GetByRole(AriaRole.Menuitem, new LocatorGetByRoleOptions { Name = "Clear" }).ClickAsync();
        await Expect(open).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        await Expect(Page.Locator("[data-testid='ui-actions-log']")).ToContainTextAsync("cleared the filters");
    });

    [Fact]
    public Task The_menu_sits_where_Position_Align_Gap_and_Offset_say() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-dropdown']");

        // Below, start edges together, five pixels off: the default.
        var options = scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Options" });
        await options.ClickAsync();
        var at = (await options.BoundingBoxAsync())!;
        var menu = (await scope.Locator("[data-ui-menu]:popover-open").BoundingBoxAsync())!;
        Assert.InRange(menu.X - at.X, -0.6f, 0.6f);
        Assert.InRange(menu.Y - (at.Y + at.Height), 4.4f, 5.6f);
        await Page.Keyboard.PressAsync("Escape");

        // Above.
        var above = scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Above" });
        await above.ClickAsync();
        at = (await above.BoundingBoxAsync())!;
        menu = (await scope.Locator("[data-ui-navmenu]:popover-open").BoundingBoxAsync())!;
        Assert.InRange(at.Y - (menu.Y + menu.Height), 4.4f, 5.6f);
        await Page.Keyboard.PressAsync("Escape");

        // Two pixels below, fifteen back past the trigger's start edge.
        var nudged = scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Offset and gap" });
        await nudged.ClickAsync();
        at = (await nudged.BoundingBoxAsync())!;
        menu = (await scope.Locator("[data-ui-navmenu]:popover-open").BoundingBoxAsync())!;
        Assert.InRange(menu.X - at.X, -15.6f, -14.4f);
        Assert.InRange(menu.Y - (at.Y + at.Height), 1.4f, 2.6f);
        await Page.Keyboard.PressAsync("Escape");

        // End edges together.
        var account = scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Olivia Martin" });
        await account.ClickAsync();
        at = (await account.BoundingBoxAsync())!;
        menu = (await scope.Locator("[data-ui-navmenu]:popover-open").BoundingBoxAsync())!;
        Assert.InRange((menu.X + menu.Width) - (at.X + at.Width), -0.6f, 0.6f);
        // A navigation menu is links, not a menu: focus stays on the button, and Tab reaches the first link.
        await Expect(account).ToBeFocusedAsync();
        await Expect(scope.Locator("[data-ui-navmenu]:popover-open a")).ToHaveCountAsync(5);
    });

    [Fact]
    public Task A_click_outside_closes_the_menu_and_presses_nothing_under_it() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-dropdown']");
        var options = scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Options" });
        var other = scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Groups" });

        await options.ClickAsync();
        await Expect(scope.Locator("[data-ui-menu]:popover-open")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // The page behind an open menu does not scroll and does not take the pointer, as on Flux's.
        Assert.Equal("hidden", await Page.EvaluateAsync<string>("() => getComputedStyle(document.documentElement).overflowY"));

        // On another dropdown's trigger: the click closes this menu and does not open that one.
        var box = (await other.BoundingBoxAsync())!;
        await Page.Mouse.ClickAsync(box.X + (box.Width / 2), box.Y + (box.Height / 2));

        await Expect(scope.Locator("[data-ui-menu]:popover-open")).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        await Expect(other).ToHaveAttributeAsync("aria-expanded", "false");
        // And focus is handed back to the trigger, which the browser's own light dismiss does not do.
        await Expect(options).ToBeFocusedAsync(new LocatorAssertionsToBeFocusedOptions { Timeout = 10_000 });
    });

    // The words of the row that has focus: where the keyboard cursor is, to a screen reader.
    private Task<string> FocusedRowAsync() =>
        Page.EvaluateAsync<string>(
            "() => { const a = document.activeElement; "
            + "return a && a.hasAttribute('data-active') && /^menuitem/.test(a.getAttribute('role') || '') ? a.textContent.trim() : ''; }");

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
        var card = scope.GetByText("Right click");
        await card.ScrollIntoViewIfNeededAsync();
        var box = (await card.BoundingBoxAsync())!;
        var x = box.X + (box.Width / 2);
        var y = box.Y + 8;
        var panel = scope.Locator("[data-ui-menu][popover]");

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

        // At the pointer as Flux puts it: below by five pixels, reaching back from it — not centred on the
        // screen the way an unplaced popover would be.
        var at = (await panel.BoundingBoxAsync())!;
        Assert.InRange(at.X + at.Width, x - 2, x + 2);
        Assert.InRange(at.Y, y + 3, y + 7);

        // The same keyboard a dropdown has: focus is on the menu, the arrows move the cursor, Enter presses the row.
        await Expect(panel).ToBeFocusedAsync(new LocatorAssertionsToBeFocusedOptions { Timeout = 10_000 });
        await Page.Keyboard.PressAsync("ArrowDown");
        await WaitForCursorAsync(FocusedRowAsync, "New post");
        await Page.Keyboard.PressAsync("Enter");

        await Expect(Page.Locator("[data-testid='ui-actions-log']")).ToContainTextAsync("new post in the card");
        await Expect(panel).ToBeHiddenAsync();
    });

    [Fact]
    public Task A_context_menu_opened_at_the_edge_of_the_viewport_stays_on_screen() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-context-menu']");
        var card = scope.GetByText("Right click");
        await card.ScrollIntoViewIfNeededAsync();
        var panel = scope.Locator("[data-ui-menu][popover]");

        // A real MouseEvent on the card, reporting a pointer at the bottom-LEFT corner of the viewport — where a
        // menu that reaches back from the pointer and hangs below it would open off both edges. The card is not
        // under that corner, so a real mouse cannot be sent there; the event is what the runtime reads either way.
        var size = Page.ViewportSize!;
        for (var attempt = 0; attempt < 10 && !await panel.IsVisibleAsync(); attempt++)
        {
            await card.EvaluateAsync(
                "(el, p) => el.dispatchEvent(new MouseEvent('contextmenu', "
                + "{ bubbles: true, cancelable: true, button: 2, clientX: p[0], clientY: p[1] }))",
                new[] { 2, size.Height - 2 });
            await Page.WaitForTimeoutAsync(300);
        }

        await Expect(panel).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        var at = (await panel.BoundingBoxAsync())!;
        Assert.True(at.X >= 0, $"the menu ran off the left edge ({at.X}).");
        Assert.True(at.Y + at.Height <= size.Height, $"the menu ran off the bottom edge ({at.Y}+{at.Height}).");
        // Pulled back only as far as it had to be: still in the corner the pointer was in, not reset to the origin.
        Assert.True(at.X < size.Width / 2 && at.Y > size.Height / 3, $"the menu left the pointer's corner ({at.X},{at.Y}).");

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
    public Task The_modal_opens_on_demand_and_closes_from_its_footer() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-modal']");
        await Expect(scope.Locator(".modal")).ToHaveCountAsync(0);

        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete order" })
            .First.ClickAsync();

        // The ELEMENT, not [role='dialog']: Ui.Modal renders a real <dialog>, which carries that role
        // implicitly, so stating it again in the markup would be the redundant ARIA guidance warns
        // against — and this selector silently matched nothing once it stopped being a div.
        var dialog = scope.Locator("dialog");
        await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(dialog).ToContainTextAsync("This cannot be undone.");

        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Cancel" }).ClickAsync();
        await Expect(scope.Locator(".modal")).ToHaveCountAsync(0);
    });

    [Fact]
    public Task The_popover_dialog_opens_and_escape_closes_it() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-modal-popover']");
        var dialog = Page.Locator("#demo-shortcuts");

        await Expect(dialog).ToBeHiddenAsync();

        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Show shortcuts" })
            .ClickAsync();
        await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // A <details> toggling INSIDE the dialog is not the dialog closing: toggle does not bubble, but the
        // runtime's capture-phase delegation used to hand it to the nearest element with a toggle handler —
        // the dialog, whose OnClose reads a closed state from it (#1116 review).
        var log = Page.Locator("[data-testid='ui-actions-log']");
        await Page.Locator("[data-testid='ui-modal-popover-more'] summary").ClickAsync();
        await Expect(Page.Locator("[data-testid='ui-modal-popover-more']")).ToHaveAttributeAsync("open", "");
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(log).Not.ToContainTextAsync("closed the shortcuts");

        // No class was written: the button names the dialog with popovertarget and the browser puts it in the
        // top layer. Escape closing it is the same mechanism, and OnClose only hears about it.
        await Page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(log).ToContainTextAsync("closed the shortcuts", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task The_dialog_is_a_real_modal_that_locks_the_scroll_and_hands_focus_back() => RunAsync(async () =>
    {
        await OpenAsync();

        var trigger = Page.Locator("[data-testid='ui-modal-popover']")
            .GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Show shortcuts" });
        var dialog = Page.Locator("#demo-shortcuts");

        await trigger.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // The invoker command, not the popover fallback: a :modal dialog, the page behind inert.
        Assert.True(await dialog.EvaluateAsync<bool>("d => d.matches(':modal')"), "the kit's dialog opened as a popover");
        // The kit's stylesheet locks the page under an open kit dialog, and only then.
        Assert.Equal("hidden", await Page.EvaluateAsync<string>("() => getComputedStyle(document.documentElement).overflow"));

        await Page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToBeHiddenAsync();
        Assert.NotEqual("hidden", await Page.EvaluateAsync<string>("() => getComputedStyle(document.documentElement).overflow"));
        await Expect(trigger).ToBeFocusedAsync();
    });

    [Fact]
    public Task The_state_driven_dialog_traps_focus_and_escape_runs_its_close_handler() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-modal']");
        var opener = scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete order" }).First;

        await opener.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");

        var dialog = scope.Locator("dialog");
        await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // The runtime's trap moved focus in; Tab cycles inside rather than reaching the page behind.
        await Expect(dialog).ToHaveAttributeAsync("data-rask-focus-trap", "");
        for (var i = 0; i < 5; i++)
        {
            await Page.Keyboard.PressAsync("Tab");
            Assert.True(
                await dialog.EvaluateAsync<bool>("d => d.contains(document.activeElement)"),
                "Tab escaped the state-driven dialog.");
        }

        // Escape presses the dismiss control, OnClose stops rendering it, and focus goes back to the opener.
        await Page.Keyboard.PressAsync("Escape");
        await Expect(scope.Locator(".modal")).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        await Expect(opener).ToBeFocusedAsync();

        // ...and it was a DISMISSAL, which OnCancel heard before OnClose (#1116).
        await Expect(Page.Locator("[data-testid='ui-actions-log']"))
            .ToContainTextAsync("dismissed the dialog", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task A_flyout_runs_the_full_height_of_the_viewport() => RunAsync(async () =>
    {
        await OpenAsync();

        await Page.Locator("[data-testid='ui-modal-flyout']")
            .GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Filters" }).ClickAsync();

        var box = Page.Locator("#demo-filters .modal-box");
        await Expect(box).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        var viewport = Page.ViewportSize!;
        var rect = await box.BoundingBoxAsync();
        Assert.True(rect!.Height >= viewport.Height - 2, $"the flyout is {rect.Height}px tall in a {viewport.Height}px viewport.");
        Assert.True(rect.X + rect.Width >= viewport.Width - 2, "the flyout is not against the end edge.");
    });

    [Fact]
    public Task The_popover_dialog_is_a_real_dialog_element() => RunAsync(async () =>
    {
        await OpenAsync();

        // A <dialog> rather than a div wearing role=dialog, which is what lets the popover path get a
        // native ::backdrop instead of one the kit paints.
        var tag = await Page.Locator("#demo-shortcuts").EvaluateAsync<string>("el => el.tagName");

        Assert.Equal("DIALOG", tag);
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
    public Task The_button_page_shows_every_Flux_example_drawn_as_Flux_draws_it() => RunAsync(async () =>
    {
        await OpenAsync();

        var variants = Page.Locator("[data-testid='ui-button-variants'] [data-ui-button]");
        var outline = variants.Nth(0);

        // Flux's base size and its default: 40px tall, 16px either side, an 8px radius, a hairline border.
        await Expect(variants).ToHaveCountAsync(6);
        await Expect(outline).ToHaveCSSAsync("height", "40px");
        await Expect(outline).ToHaveCSSAsync("padding-left", "16px");
        await Expect(outline).ToHaveCSSAsync("border-top-left-radius", "8px");
        await Expect(outline).ToHaveCSSAsync("border-top-width", "1px");
        await Expect(Page.Locator("[data-testid='ui-button-outline-colors'] [data-ui-button]")).ToHaveCountAsync(17);
        await Expect(Page.Locator("[data-testid='ui-button-colors'] [data-ui-button]")).ToHaveCountAsync(10);
        await Expect(Page.Locator("[data-testid='ui-button-sizes'] [data-ui-button]").Nth(2)).ToHaveCSSAsync("height", "24px");
        await Expect(Page.Locator("[data-testid='ui-button-full-width'] [data-ui-button]")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-testid='ui-button-inset'] [data-ui-button]")).ToHaveCSSAsync("margin-top", "-6px");
    });

    [Fact]
    public Task An_icon_pads_its_own_side_less_and_a_button_with_only_an_icon_is_a_square() => RunAsync(async () =>
    {
        await OpenAsync();

        var icons = Page.Locator("[data-testid='ui-button-icons']");
        var export = icons.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Export" });
        var more = icons.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "More" });

        await Expect(export).ToHaveCSSAsync("padding-left", "12px");
        await Expect(export).ToHaveCSSAsync("padding-right", "16px");
        await Expect(export.Locator("svg")).ToHaveCSSAsync("width", "16px");
        await Expect(more).ToHaveCSSAsync("width", "40px");
        await Expect(more.Locator("svg")).ToHaveCSSAsync("width", "20px");
    });

    [Fact]
    public Task Grouped_buttons_share_one_border_and_keep_their_corners_only_at_the_ends() => RunAsync(async () =>
    {
        await OpenAsync();

        var group = Page.Locator("[data-testid='ui-button-groups'] [data-ui-button-group]").First;
        var first = group.Locator("[data-ui-button]").Nth(0);
        var middle = group.Locator("[data-ui-button]").Nth(1);
        var last = group.Locator("[data-ui-button]").Nth(2);

        await Expect(first).ToHaveCSSAsync("border-top-left-radius", "8px");
        await Expect(first).ToHaveCSSAsync("border-top-right-radius", "0px");
        await Expect(middle).ToHaveCSSAsync("border-left-width", "0px");
        await Expect(middle).ToHaveCSSAsync("border-top-left-radius", "0px");
        await Expect(last).ToHaveCSSAsync("border-top-right-radius", "8px");
    });

    [Fact]
    public Task A_tooltip_shows_while_its_button_is_hovered() => RunAsync(async () =>
    {
        await OpenAsync();

        var add = Page.Locator("[data-testid='ui-button-icons']")
            .GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Add" });
        var tip = add.Locator("[data-ui-tooltip-content]");

        await Expect(tip).ToBeHiddenAsync();
        await add.HoverAsync();

        await Expect(tip).ToBeVisibleAsync();
        await Expect(tip).ToContainTextAsync("Add");
    });

    [Fact]
    public Task An_icon_only_button_still_has_a_name() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-button']");

        // A square holds one glyph, so its label cannot be visible text. Reaching it by accessible name
        // is the proof it is still announced rather than read out as "button" — "Add" by its tooltip.
        await Expect(scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Close" }))
            .ToBeVisibleAsync();
        await Expect(scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Add" }))
            .ToBeVisibleAsync();
    });

    [Fact]
    public Task A_disabled_button_is_disabled_by_the_browser_rather_than_by_a_class() => RunAsync(async () =>
    {
        await OpenAsync();

        // By attribute, so the browser refuses the press: a class alone still takes the click.
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
        // takes — and the button draws it as Flux does: the label fades out where it stands, so the width
        // holds, and the spinner fades in over it.
        await Expect(save).ToHaveAttributeAsync("aria-busy", "true", new LocatorAssertionsToHaveAttributeOptions { Timeout = 5_000 });
        await Expect(save.Locator("span").First).ToHaveCSSAsync("opacity", "0");
        await Expect(save.Locator("[data-ui-loading-indicator]")).ToHaveCSSAsync("opacity", "1");
        Assert.Equal("none", await save.EvaluateAsync<string>("el => getComputedStyle(el).pointerEvents"));
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
