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
    public Task A_toast_appears_on_demand_and_can_be_dismissed() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-toast']");

        // Scoped to the toast's own section, not the page: any other polite announcement on the page
        // would match a page-wide locator too, and go on matching after the toast was dismissed.
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
    public Task An_open_tooltip_is_visible_without_a_hover() => RunAsync(async () =>
    {
        await OpenAsync();

        // The only way a touch user ever sees one. Asserted through the element daisyUI draws the
        // bubble with, since the tip lives in an attribute rather than in text.
        var open = Page.Locator("[data-testid='ui-tooltip'] .tooltip-open");

        await Expect(open).ToHaveCountAsync(1);
        await Expect(open).ToHaveAttributeAsync("data-tip", "Always shown");
    });

    [Fact]
    public Task A_tooltip_shows_on_a_tap_on_a_disabled_button_and_carries_its_shortcut() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-tooltip']");

        // Toggleable: focus — which a tap gives the wrapper — is what shows it, measured on the pseudo-element
        // daisyUI draws the bubble with.
        var tap = scope.Locator(".ui-tooltip-toggleable");
        const string BubbleOpacity = "el => getComputedStyle(el, '::before').opacity";
        Assert.Equal("0", await tap.EvaluateAsync<string>(BubbleOpacity));
        await tap.FocusAsync();
        await OpacityReachesOneAsync(tap, BubbleOpacity);

        // A disabled .btn takes no pointer events, so the hover lands on the wrapper and the tip still shows.
        var disabled = scope.Locator(".tooltip", new LocatorLocatorOptions { Has = Page.Locator("button[disabled]") });
        await disabled.HoverAsync();
        await OpacityReachesOneAsync(disabled, BubbleOpacity);

        // The shortcut is a real <kbd> inside the tip.
        await Expect(scope.Locator(".tooltip-content[role='tooltip'] kbd")).ToHaveTextAsync("⌘S");
    });

    // The bubble fades in over daisyUI's 200 ms transition, so the computed opacity is polled rather than read once.
    private static async Task OpacityReachesOneAsync(ILocator tooltip, string read)
    {
        var last = "";
        for (var i = 0; i < 50; i++)
        {
            last = await tooltip.EvaluateAsync<string>(read);
            if (last == "1")
            {
                return;
            }

            await Task.Delay(100);
        }

        Assert.Fail($"the tooltip bubble never became visible (opacity {last}).");
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
