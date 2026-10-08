using System.Text.RegularExpressions;
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
                     "ui-callout", "ui-loading", "ui-progress", "ui-tooltip", "ui-skeleton", "ui-toast",
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
    public Task Every_Flux_callout_example_is_drawn_and_a_dismissed_one_leaves() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-callout']");
        var callouts = scope.Locator("[data-ui-callout]");

        // Flux's page, example for example: 3 basics, 3 with actions, 2 dismissible, 4 variants, 18 colours
        // and 4 spotlights. None announces itself — they are all on the page when it loads.
        await Expect(callouts).ToHaveCountAsync(34, new LocatorAssertionsToHaveCountOptions { Timeout = 15_000 });
        await Expect(scope.Locator("[data-ui-callout][role]")).ToHaveCountAsync(0);

        // The kit's sheet reached the page: a danger callout is red-50 with a 12px corner, not an unstyled div.
        var danger = scope.Locator("[data-example='Variants'] [data-ui-callout]").Last;
        Assert.Equal("12px", await danger.EvaluateAsync<string>("e => getComputedStyle(e).borderTopLeftRadius"));
        Assert.NotEqual("rgba(0, 0, 0, 0)", await danger.EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor"));

        // Inline puts the actions beside the content: the button's top is above the heading's bottom.
        var inline = scope.Locator("[data-example='Inline actions'] [data-ui-callout]").First;
        var heading = await inline.Locator("[data-slot='heading']").BoundingBoxAsync();
        var action = await inline.Locator("[data-slot='actions'] button").First.BoundingBoxAsync();
        Assert.True(action!.Y < heading!.Y + heading.Height, "the inline actions were stacked under the heading.");

        // Dismissing is the page's: the control is the callout's, the field it clears is the demo's.
        var dismissible = scope.Locator("[data-example='Dismissible']");
        await dismissible.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Dismiss" }).First.ClickAsync();
        await Expect(dismissible.Locator("[data-ui-callout]")).ToHaveCountAsync(1);
        await dismissible.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Show them again" }).ClickAsync();
        await Expect(dismissible.Locator("[data-ui-callout]")).ToHaveCountAsync(2);
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
    public Task A_progress_bar_announces_its_value_and_draws_that_share_of_its_track() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-progress']");
        var progress = scope.Locator("[data-testid='ui-progress-storage']");

        await Expect(progress).ToHaveRoleAsync(AriaRole.Progressbar);
        await Expect(progress).ToHaveAttributeAsync("aria-valuenow", "62");

        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "+10" }).ClickAsync();

        // The announced value, and the bar: 72% of the track once the 300ms move has finished.
        await Expect(progress).ToHaveAttributeAsync("aria-valuenow", "72");
        await Expect(progress).ToHaveAttributeAsync("style", new Regex("--ui-progress-percentage:72%"));
        await Page.WaitForTimeoutAsync(400);
        var track = await progress.BoundingBoxAsync();
        var bar = await progress.Locator("div").BoundingBoxAsync();
        Assert.Equal(0.72, Math.Round(bar!.Width / track!.Width, 2));
    });

    [Fact]
    public Task A_shimmering_skeleton_is_drawn_and_its_light_is_moving() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-skeleton']");
        await Expect(scope.Locator("[data-ui-skeleton-group]")).ToHaveCountAsync(3);

        // The avatar is the call site's size, and the group's shimmer reached it: a ::before running the
        // kit's keyframe is the check that both the utilities and the keyframe are in the compiled sheet.
        var avatar = scope.Locator("[data-ui-skeleton]").First;
        var box = await avatar.BoundingBoxAsync();
        Assert.Equal(40, box!.Width);
        Assert.Equal(40, box.Height);
        Assert.Equal(
            "ui-shimmer 2s",
            await avatar.EvaluateAsync<string>(
                "el => { const s = getComputedStyle(el, '::before'); return s.animationName + ' ' + s.animationDuration; }"));
    });

    [Fact]
    public Task A_raised_toast_shows_in_the_top_layer_and_its_close_button_takes_it_down() => RunAsync(async () =>
    {
        await OpenAsync();
        var toast = Page.Locator("[data-ui-toast] > [data-ui-toast-dialog]");

        // No assertion that it starts absent, deliberately. The harness re-runs this body on a boot
        // failure (RaceAgainstBootFailureAsync), and the second attempt gets a page the first one had
        // already raised a toast on. What the component owes is the TRANSITION, which is what is asserted.
        await Page.Locator("#toast-permanent").ClickAsync();
        await Page.Locator("#toast-success").ClickAsync();

        // One at a time: the success toast took the permanent one's place.
        await Expect(toast).ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        await Expect(toast).ToHaveAttributeAsync("data-variant", "success");
        await Expect(toast).ToContainTextAsync("Post created");
        // A popover the runtime showed, so it is over an open dialog rather than under it.
        Assert.True(await Page.Locator("[data-ui-toast]").EvaluateAsync<bool>("host => host.matches(':popover-open')"));

        await toast.Locator("[data-rask-dismiss]").ClickAsync();
        await Expect(toast).ToHaveCountAsync(0);
    });

    [Fact]
    public Task A_timed_toast_goes_by_itself_and_a_permanent_one_stays() => RunAsync(async () =>
    {
        await OpenAsync();
        var toast = Page.Locator("[data-ui-toast] > [data-ui-toast-dialog]");

        await Page.Locator("#toast-brief").ClickAsync();
        await Expect(toast).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        // One second, then the fade, then the runtime presses its close button.
        await Expect(toast).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });

        await Page.Locator("#toast-permanent").ClickAsync();
        await Expect(toast).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Page.WaitForTimeoutAsync(2_500);
        await Expect(toast).ToHaveCountAsync(1);

        // Escape takes a toast on its own down.
        await Page.Keyboard.PressAsync("Escape");
        await Expect(toast).ToHaveCountAsync(0);
    });

    [Fact]
    public Task Inside_a_group_toasts_stack_behind_the_newest_and_open_under_the_pointer() => RunAsync(async () =>
    {
        await OpenAsync();
        await Page.Locator("#toast-layout-stack").ClickAsync();
        var toasts = Page.Locator("[data-ui-toast-group] > [data-ui-toast-dialog]");

        await Page.Locator("#toast-permanent").ClickAsync();
        await Page.Locator("#toast-permanent").ClickAsync();
        await Expect(toasts).ToHaveCountAsync(2, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });

        // A deck: the older one settles ten pixels up and five percent narrower, behind the front one.
        await Page.WaitForFunctionAsync(
            """
            () => {
                const [front, behind] = [...document.querySelectorAll('[data-ui-toast-group] > [data-ui-toast-dialog] > div')]
                    .map(card => card.getBoundingClientRect());
                return Math.abs(front.top - behind.top - 10) < 0.5 && Math.abs(behind.width - front.width * 0.95) < 0.5;
            }
            """);

        // Under the pointer each sits on the one in front of it.
        await toasts.Nth(0).HoverAsync();
        await Page.WaitForFunctionAsync(
            """
            () => {
                const cards = document.querySelectorAll('[data-ui-toast-group] > [data-ui-toast-dialog] > div');
                return cards[1].getBoundingClientRect().bottom <= cards[0].getBoundingClientRect().top;
            }
            """);
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

    // The hooks are not in rask.wasm.js: the runtime fetches them from beside itself when a page asks for one
    // (docs/js-interop-runtime.md, "How the hooks load"). In a WebAssembly app that is a classic script next to
    // the module, and however many hooked elements and renders a page sees, it is asked for once.
    [Fact]
    public Task The_behaviour_hooks_are_fetched_once_from_beside_the_runtime_and_a_tooltip_answers() => RunAsync(async () =>
    {
        await OpenTooltipsAsync();

        var tooltip = Page.Locator("[data-testid='ui-tooltip'] > [data-ui-tooltip]").First;
        await tooltip.Locator("button").HoverAsync();
        await Expect(tooltip.Locator("[data-ui-tooltip-content]")).ToBeVisibleAsync();
        var requested = await Page.EvaluateAsync<string[]>(
            "() => performance.getEntriesByType('resource').map(e => new URL(e.name).pathname).filter(p => p.includes('rask-hooks'))");
        var runtime = await Page.EvaluateAsync<string[]>(
            "() => performance.getEntriesByType('resource').map(e => new URL(e.name).pathname).filter(p => p.endsWith('/rask.wasm.js'))");

        Assert.Equal(["/rask-hooks.js"], requested);
        Assert.Equal(["/rask.wasm.js"], runtime.Distinct());
        Assert.Equal(1, await Page.Locator("script[data-rask-hooks]").CountAsync());
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
    public Task Escape_dismisses_a_hovered_tooltip_and_a_press_keeps_it_away_until_the_pointer_returns() => RunAsync(async () =>
    {
        await OpenTooltipsAsync();

        // The one around a button with text: not an icon button, and under the pointer rather than focused.
        var tooltip = Page.Locator("[data-testid='ui-tooltip'] > [data-ui-tooltip]", new PageLocatorOptions { HasTextString = "Shortcut" });
        var trigger = tooltip.Locator("button");
        var content = tooltip.Locator("[data-ui-tooltip-content]");
        await trigger.HoverAsync();
        await Expect(content).ToBeVisibleAsync();

        // Flux's four: Escape under the pointer, back on the next visit, gone on a press and still gone once
        // the press is released — until the pointer has left and come back.
        await Page.Keyboard.PressAsync("Escape");
        await Expect(content).ToBeHiddenAsync();
        await Page.Mouse.MoveAsync(2, 2);
        await trigger.HoverAsync();
        await Expect(content).ToBeVisibleAsync();
        await Page.Mouse.DownAsync();
        await Expect(content).ToBeHiddenAsync();
        await Page.Mouse.UpAsync();
        await Page.Mouse.MoveAsync((await trigger.BoundingBoxAsync())!.X + 4, (await trigger.BoundingBoxAsync())!.Y + 4);
        await Expect(content).ToBeHiddenAsync();
        await Page.Mouse.MoveAsync(2, 2);
        await trigger.HoverAsync();
        await Expect(content).ToBeVisibleAsync();
    });

    [Fact]
    public Task A_tooltip_shown_by_keyboard_focus_stays_when_the_pointer_passes_over_and_leaves() => RunAsync(async () =>
    {
        await OpenTooltipsAsync();

        var tooltip = Page.Locator("[data-testid='ui-tooltip'] > [data-ui-tooltip]").First;
        var content = tooltip.Locator("[data-ui-tooltip-content]");
        await Page.Keyboard.PressAsync("Shift");
        await tooltip.Locator("button").FocusAsync();
        await Expect(content).ToBeVisibleAsync();

        await tooltip.Locator("button").HoverAsync();
        await Page.Mouse.MoveAsync(2, 2);

        // A reader who tabbed here and nudged the mouse still needs the label; blur is what takes it away.
        await Expect(content).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Tab");
        await Expect(content).ToBeHiddenAsync();
    });

    [Fact]
    public Task The_pointer_over_a_stack_holds_every_toast_in_it_and_each_goes_once_it_has_left() => RunAsync(async () =>
    {
        await OpenAsync();
        await Page.Locator("#toast-layout-stack").ClickAsync();
        var toasts = Page.Locator("[data-ui-toast-group] > [data-ui-toast-dialog]");

        // Two one-second toasts, and the pointer on the front one before either has run out.
        await Page.Locator("#toast-brief").ClickAsync();
        await Page.Locator("#toast-brief").ClickAsync();
        await Expect(toasts).ToHaveCountAsync(2, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
        await toasts.Nth(0).Locator("> div").HoverAsync();
        await Page.WaitForTimeoutAsync(3000);

        // Three seconds on, both are there and neither has faded: the one the pointer is not on is held too.
        await Expect(toasts).ToHaveCountAsync(2);
        Assert.Equal(
            ["1", "1"],
            await toasts.EvaluateAllAsync<string[]>("all => all.map(toast => getComputedStyle(toast.firstElementChild).opacity)"));
        await Page.Mouse.MoveAsync(2, 2);
        await Expect(toasts).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10_000 });
    });

    [Fact]
    public Task A_toast_with_focus_inside_it_still_goes_on_time() => RunAsync(async () =>
    {
        await OpenAsync();
        var toast = Page.Locator("[data-ui-toast] > [data-ui-toast-dialog]");
        await Page.Locator("#toast-brief").ClickAsync();
        await Expect(toast).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        // Measured on Flux: focus holds nothing. The pointer is parked where it is over no toast.
        await Page.Mouse.MoveAsync(2, 2);
        await toast.Locator("[data-rask-dismiss]").FocusAsync();

        await Expect(toast).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });
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
