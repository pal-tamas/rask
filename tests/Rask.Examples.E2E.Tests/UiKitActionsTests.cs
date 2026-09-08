using Microsoft.Playwright;
using Rask.Examples.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

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
                     "ui-button", "ui-dropdown", "ui-modal", "ui-swap", "ui-theme-controller",
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

        var dropdown = Page.Locator("[data-testid='ui-dropdown'] .dropdown").First;
        var trigger = dropdown.Locator("button").First;

        // Closed writes `dropdown-close`, not merely the absence of `dropdown-open` — because daisyUI
        // also opens on :focus-within, and clicking the trigger puts focus inside it. Without the
        // explicit close class this assertion would fail the moment the click landed.
        await Expect(dropdown).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("dropdown-close"));
        await Expect(trigger).ToHaveAttributeAsync("aria-expanded", "false");

        await trigger.ClickAsync();
        await Expect(dropdown).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("dropdown-open"));
        await Expect(trigger).ToHaveAttributeAsync("aria-expanded", "true");

        // The panel is genuinely reachable once open, which is the whole point of the class.
        await Expect(dropdown.Locator(".dropdown-content")).ToBeVisibleAsync();

        await trigger.ClickAsync();
        await Expect(dropdown).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("dropdown-close"));
    });

    [Fact]
    public Task ChoosingAMenuActionClosesTheDropdownAndReportsIt() => RunAsync(async () =>
    {
        await OpenAsync();

        var dropdown = Page.Locator("[data-testid='ui-dropdown'] .dropdown").First;
        await dropdown.Locator("button").First.ClickAsync();
        await dropdown.GetByText("Duplicate").ClickAsync();

        // Both halves matter: the action ran, and the menu closed itself afterwards. Closing on
        // completion is exactly what the <details> version could not do.
        await Expect(Page.Locator("[data-testid='ui-actions-log']")).ToContainTextAsync("duplicate");
        await Expect(dropdown).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("dropdown-close"));
    });

    [Fact]
    public Task TheModalOpensOnDemandAndClosesFromItsFooter() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-modal']");
        await Expect(scope.Locator(".modal")).ToHaveCountAsync(0);

        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Delete order" })
            .First.ClickAsync();

        var dialog = scope.Locator("[role='dialog']");
        await Expect(dialog).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(dialog).ToContainTextAsync("This cannot be undone.");

        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Cancel" }).ClickAsync();
        await Expect(scope.Locator(".modal")).ToHaveCountAsync(0);
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

    private async Task OpenAsync()
    {
        await Page.GotoAsync(Docs);
        await Expect(Page.Locator(".side-nav a.side-nav-link.active").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await ClickSidebar("Actions");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Actions",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
    }
}
