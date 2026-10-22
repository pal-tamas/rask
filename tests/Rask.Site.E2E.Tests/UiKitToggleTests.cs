using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     Flux's toggle on the running site, under a real pointer and real keys.
/// </summary>
/// <remarks>
///     What a unit test cannot see: that Space and Enter turn it as a press does, that the stylesheet swaps the
///     drawing on <c>data-checked</c>, that a bound toggle that saves waits visibly, and that its tooltip names it.
/// </remarks>
[Collection(WasmExampleCollection.Name)]
public sealed class UiKitToggleTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    // Notes, from before the press, whether the runtime ever marked the button waiting. A look taken AFTER the press
    // can miss the whole save: the first handler a WebAssembly page runs keeps the browser's one thread long enough
    // on a slow machine for the mark to come and go before the next look gets in.
    private const string Watch =
        "toggle => { window.__toggleWaited = false; new MutationObserver(() => { "
        + "if (toggle.hasAttribute('data-loading')) window.__toggleWaited = true; })"
        + ".observe(toggle, { attributes: true, attributeFilter: ['data-loading'] }); }";

    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Wasm";
    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task A_press_Space_and_Enter_each_turn_it_and_the_solid_drawing_shows_only_while_it_is_on() => RunAsync(async () =>
    {
        var scope = await OpenAsync();
        var toggle = scope.Locator("[data-testid='ui-toggle-basic'] [data-ui-toggle]");
        var solid = toggle.Locator("svg[fill='currentColor']");
        var outline = toggle.Locator("svg[stroke='currentColor']");
        await Expect(toggle).ToHaveAttributeAsync("aria-checked", "false");
        await Expect(solid).ToBeHiddenAsync();

        await toggle.ClickAsync();

        // Nobody listens to this one, so the browser turns it: the button carries no handler at all.
        await Expect(toggle).ToHaveAttributeAsync("aria-checked", "true");
        await Expect(toggle).ToHaveAttributeAsync("data-checked", "");
        await Expect(toggle).ToHaveAttributeAsync("data-rask-switch", "");
        await Expect(toggle).Not.ToHaveAttributeAsync("data-rask-on-click", new System.Text.RegularExpressions.Regex(".*"));
        await Expect(solid).ToBeVisibleAsync();
        await Expect(outline).ToBeHiddenAsync();
        await toggle.FocusAsync();
        await Page.Keyboard.PressAsync("Space");
        await Expect(toggle).ToHaveAttributeAsync("aria-checked", "false");
        await Expect(outline).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(toggle).ToHaveAttributeAsync("aria-checked", "true");
        // An arrow is not its key, and the keys leave the focus where it was.
        await Page.Keyboard.PressAsync("ArrowRight");
        await Expect(toggle).ToHaveAttributeAsync("aria-checked", "true");
        await Expect(toggle).ToBeFocusedAsync();
    });

    [Fact]
    public Task A_toggle_is_a_switch_named_by_its_tooltip_which_shows_under_the_pointer() => RunAsync(async () =>
    {
        var scope = await OpenAsync();
        var toggle = scope.Locator("[data-testid='ui-toggle-basic'] [data-ui-toggle]");
        var tip = scope.Locator("[data-testid='ui-toggle-basic'] [data-ui-tooltip-content]");

        await toggle.HoverAsync();

        await Expect(tip).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Expect(tip).ToHaveTextAsync("Fast mode");
        await Expect(toggle).ToHaveRoleAsync(AriaRole.Switch);
        await Expect(toggle).ToHaveAccessibleNameAsync("Fast mode");
        Assert.Equal(40, (await toggle.BoundingBoxAsync())!.Width, 0.5);
    });

    [Fact]
    public Task A_bound_toggle_that_saves_shows_its_spinner_while_it_waits_and_the_page_hears_the_written_value() => RunAsync(async () =>
    {
        var scope = await OpenAsync();
        var toggle = scope.Locator("[data-testid='ui-toggle-saving'] [data-ui-toggle]");
        var says = scope.Locator("[data-testid='ui-toggle-saves']");
        await Expect(says).ToContainTextAsync("Saved as inactive · 0 saves");

        await toggle.EvaluateAsync(Watch);

        await toggle.ClickAsync();

        // The member is written before the page's handler runs, and the handler takes 600 ms: the runtime marks
        // the button after 200, and takes the mark off when the answer is in.
        await Expect(says).ToContainTextAsync("Saved as active · 1 saves", new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
        Assert.True(await Page.EvaluateAsync<bool>("() => window.__toggleWaited"), "the toggle was never marked data-loading while it saved.");
        await Expect(toggle).Not.ToHaveAttributeAsync("data-loading", "");
        // What the mark draws, held still rather than caught in passing: the spinner in the icon's place, no pointer.
        await toggle.EvaluateAsync("toggle => toggle.setAttribute('data-loading', '')");
        await Expect(toggle.Locator("[data-ui-loading-indicator]")).ToHaveCSSAsync("opacity", "1");
        await Expect(toggle.Locator("svg[fill='currentColor']")).ToHaveCSSAsync("opacity", "0");
        await Expect(toggle).ToHaveCSSAsync("pointer-events", "none");
        await toggle.EvaluateAsync("toggle => toggle.removeAttribute('data-loading')");
        await Expect(toggle).ToHaveAttributeAsync("aria-checked", "true");
    });

    [Fact]
    public Task A_page_that_keeps_the_state_hears_each_turn_and_stateful_labels_follow_their_own() => RunAsync(async () =>
    {
        var scope = await OpenAsync();
        var sound = scope.Locator("[data-testid='ui-toggle-stateful-icon'] [data-ui-toggle]");
        var labelled = scope.Locator("[data-testid='ui-toggle-stateful-label'] [data-ui-toggle]");
        await Expect(scope.Locator("[data-testid='ui-toggle-sound']")).ToHaveTextAsync("Muted");
        await Expect(labelled.Locator("span:visible")).ToHaveTextAsync("Sound off");

        await sound.ClickAsync();
        await labelled.ClickAsync();

        await Expect(scope.Locator("[data-testid='ui-toggle-sound']")).ToHaveTextAsync("Playing");
        await Expect(sound).ToHaveAttributeAsync("aria-checked", "true");
        await Expect(labelled.Locator("span:visible")).ToHaveTextAsync("Sound on");
    });

    [Fact]
    public Task A_disabled_toggle_is_dimmed_and_takes_no_press() => RunAsync(async () =>
    {
        var scope = await OpenAsync();
        var disabled = scope.Locator("[data-testid='ui-toggle-disabled'] [data-ui-toggle]");
        await disabled.ScrollIntoViewIfNeededAsync();

        await disabled.ClickAsync(new LocatorClickOptions { Force = true });

        await Expect(disabled).ToBeDisabledAsync();
        await Expect(disabled).ToHaveCSSAsync("opacity", "0.5");
        await Expect(disabled).ToHaveAttributeAsync("aria-checked", "false");
    });

    private async Task<ILocator> OpenAsync()
    {
        await OpenUiPageAsync("toggle", "Toggle");

        var scope = Page.Locator("main");
        await Expect(scope).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await scope.ScrollIntoViewIfNeededAsync();
        return scope;
    }
}
