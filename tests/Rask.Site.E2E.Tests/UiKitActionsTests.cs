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
    public Task EveryActionComponentRendersWithARealSize() => RunAsync(async () =>
    {
        await OpenAsync();

        // The FAB is absent here on purpose: it is `position: fixed`, so its wrapper in the flow has no
        // height of its own and a bounding box on the wrapper would measure nothing. It is measured
        // below, on the floating element itself.
        foreach (var id in new[]
                 {
                     "ui-button", "ui-dropdown", "ui-context-menu", "ui-modal", "ui-modal-popover", "ui-swap",
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
    public Task TheDropdownOpensAndClosesFromCSharpState() => RunAsync(async () =>
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
    public Task ChoosingAMenuActionClosesTheDropdownAndReportsIt() => RunAsync(async () =>
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
    public Task TheMenuIsDrivenByTheKeyboardIntoASubmenu() => RunAsync(async () =>
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
    public Task APointerCrossingDiagonallyIntoASubmenuKeepsItOpen() => RunAsync(async () =>
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
    public Task ACheckboxItemTogglesAndKeepsTheMenuOpen() => RunAsync(async () =>
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
    public Task ARightClickOpensTheMenuAtThePointerWithTheMenuKeyboard() => RunAsync(async () =>
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
    public Task AContextMenuOpenedAtTheEdgeOfTheViewportStaysOnScreen() => RunAsync(async () =>
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
    public Task TheModalOpensOnDemandAndClosesFromItsFooter() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-modal']");
        await Expect(scope.Locator(".modal")).ToHaveCountAsync(0);

        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete order" })
            .First.ClickAsync();

        // The ELEMENT, not [role='dialog']: UiModal renders a real <dialog>, which carries that role
        // implicitly, so stating it again in the markup would be the redundant ARIA guidance warns
        // against — and this selector silently matched nothing once it stopped being a div.
        var dialog = scope.Locator("dialog");
        await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(dialog).ToContainTextAsync("This cannot be undone.");

        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Cancel" }).ClickAsync();
        await Expect(scope.Locator(".modal")).ToHaveCountAsync(0);
    });

    [Fact]
    public Task ThePopoverDialogOpensAndEscapeClosesIt() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-modal-popover']");
        var dialog = Page.Locator("#demo-shortcuts");

        await Expect(dialog).ToBeHiddenAsync();

        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Show shortcuts" })
            .ClickAsync();
        await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // Nothing in C# ran and no class was written: the button names the dialog with popovertarget
        // and the browser puts it in the top layer. Escape closing it is the same mechanism — which is
        // the whole reason this is the default path rather than the state-driven one.
        await Page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToBeHiddenAsync();
    });

    [Fact]
    public Task TheDialogIsARealModalThatLocksTheScrollAndHandsFocusBack() => RunAsync(async () =>
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
    public Task TheStateDrivenDialogTrapsFocusAndEscapeRunsItsCloseHandler() => RunAsync(async () =>
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

        // Escape presses the close control, OnClose stops rendering it, and focus goes back to the opener.
        await Page.Keyboard.PressAsync("Escape");
        await Expect(scope.Locator(".modal")).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        await Expect(opener).ToBeFocusedAsync();
    });

    [Fact]
    public Task AFlyoutRunsTheFullHeightOfTheViewport() => RunAsync(async () =>
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
    public Task ThePopoverDialogIsARealDialogElement() => RunAsync(async () =>
    {
        await OpenAsync();

        // A <dialog> rather than a div wearing role=dialog, which is what lets the popover path get a
        // native ::backdrop instead of one the kit paints.
        var tag = await Page.Locator("#demo-shortcuts").EvaluateAsync<string>("el => el.tagName");

        Assert.Equal("DIALOG", tag);
    });

    [Fact]
    public Task TheSwapFlipsItsFaceAndSaysSo() => RunAsync(async () =>
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
    public Task TheFloatingActionButtonRevealsItsActionsOnFocus() => RunAsync(async () =>
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
    public Task TheThemeControlChoosesAThemeAndThePageAppliesIt() => RunAsync(async () =>
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
    public Task AnIconOnlyButtonStillHasAName() => RunAsync(async () =>
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
    public Task ADisabledButtonIsDisabledByTheBrowserRatherThanByAClass() => RunAsync(async () =>
    {
        await OpenAsync();

        // daisyUI's btn-disabled styles without disabling: a button carrying only that class still takes
        // the click and still reaches its handler.
        var disabled = Page.Locator("[data-testid='ui-button']")
            .GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Disabled" });

        await Expect(disabled).ToBeDisabledAsync();
    });

    [Fact]
    public Task AButtonWaitingOnItsHandlerShowsASpinnerKeepsItsWidthAndTakesOnePress() => RunAsync(async () =>
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
    public Task AButtonGivenARouteNavigatesWithoutReloadingTheApp() => RunAsync(async () =>
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
