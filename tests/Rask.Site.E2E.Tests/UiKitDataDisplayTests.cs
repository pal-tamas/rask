using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     The kit's Data display components, in a browser — the size check no markup assertion can make,
///     and the state changes no static render can show.
/// </summary>
[Collection(WasmExampleCollection.Name)]
public sealed class UiKitDataDisplayTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Wasm";
    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task Every_data_display_component_renders_with_a_real_size() => RunAsync(async () =>
    {
        await OpenAsync();

        foreach (var id in new[]
                 {
                     "ui-accordion", "ui-collapse", "ui-aura", "ui-text-rotate", "ui-hover-3d",
                     "ui-hover-gallery", "ui-console-pieces", "ui-chart", "ui-display-rest",
                 })
        {
            var node = Page.Locator($"[data-testid='{id}']");
            await Expect(node).ToBeVisibleAsync(
                new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

            var box = await node.BoundingBoxAsync();
            Assert.NotNull(box);
            Assert.True(box!.Height > 0, $"{id} rendered with zero height, which is what an unstyled kit component looks like.");
        }
    });

    [Fact]
    public Task The_accordion_opens_one_section_at_a_time() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-accordion']");
        var state = Page.Locator("[data-testid='ui-accordion-state']");

        await Expect(state).ToContainTextAsync("ship");
        await Expect(scope.GetByText("Ships within two working days")).ToBeVisibleAsync();

        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Payment" }).ClickAsync();

        // The page names the open section, which is exactly what a run of <details name> could not do:
        // the browser closed the others without telling anyone which one won.
        await Expect(state).ToContainTextAsync("pay");
        await Expect(scope.GetByText("Card or bank transfer")).ToBeVisibleAsync();
        await Expect(scope.GetByText("Ships within two working days")).ToBeHiddenAsync();
    });

    [Fact]
    public Task Pressing_the_open_section_closes_everything() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-accordion']");

        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Shipping" }).ClickAsync();

        await Expect(Page.Locator("[data-testid='ui-accordion-state']")).ToContainTextAsync("All sections closed");
    });

    [Fact]
    public Task The_collapse_opens_and_closes_from_CSharp_state() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-collapse']");
        var body = scope.GetByText("Nothing in here is required.");

        await Expect(body).ToBeHiddenAsync();

        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Advanced settings" })
            .ClickAsync();

        await Expect(body).ToBeVisibleAsync();
    });

    // TheRotatorShowsEveryWordInTheMarkup moved DOWN to Rask.Ui.Tests.Components.UiTextRotateTests.
    // Its own comment gave the reason: the words are in the DOM "whatever the browser is doing with
    // them", which makes it a claim about rendered markup, and it was paying a published bundle and a
    // Chromium page to read four words out of a string. The unit test asserts more than it did — the
    // ORDER of the words, and the inner wrapper daisyUI counts children on — in under a millisecond.
    //
    // It was the only one of the forty-two UiKit journeys that could move. The rest assert CSS
    // visibility, layout geometry, real input, keyboard and focus, none of which survive that trip.

    [Fact]
    public Task The_decorative_components_announce_nothing() => RunAsync(async () =>
    {
        await OpenAsync();

        // An aura that claimed a role would put a meaningless announcement between a screen-reader user
        // and the content it is drawn around.
        var aura = Page.Locator("[data-testid='ui-aura'] .aura").First;

        await Expect(aura).ToBeVisibleAsync();
        Assert.Null(await aura.GetAttributeAsync("role"));
        Assert.Null(await aura.GetAttributeAsync("aria-label"));
    });

    [Fact]
    public Task A_linked_card_is_one_link_holding_its_figures() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-console-pieces']");
        var card = scope.Locator("a").Filter(new LocatorFilterOptions { HasText = "Jobs" });

        // One link, and the figures are inside it rather than beside it.
        await Expect(card).ToHaveCountAsync(1);
        await Expect(card).ToContainTextAsync("Outstanding");
        Assert.EndsWith("/ui/data-grid/", await card.GetAttributeAsync("href") ?? "", StringComparison.Ordinal);
    });

    [Fact]
    public Task On_a_phone_a_mono_badge_wraps_inside_its_card_instead_of_widening_it() => RunAsync(async () =>
    {
        await OpenAsync();

        try
        {
            await Page.SetViewportSizeAsync(390, 844);

            var badge = Page.Locator("[data-testid='ui-console-pieces'] .badge.font-mono");
            await Expect(badge).ToBeVisibleAsync();

            // A badge is a fixed-height pill that never breaks; a 40-character request id in one used to
            // push its row wider than the screen. The mono form gives up the height and breaks anywhere.
            var inside = await badge.EvaluateAsync<bool>(
                "el => el.getBoundingClientRect().right <= el.parentElement.getBoundingClientRect().right + 0.5");
            Assert.True(inside, "the mono badge overflowed its card at 390px");
        }
        finally
        {
            await Page.SetViewportSizeAsync(1280, 720);
        }
    });

    [Fact]
    public Task A_highlight_marks_each_match_and_shows_no_marker_characters() => RunAsync(async () =>
    {
        await OpenAsync();

        var highlight = Page.Locator("[data-testid='ui-highlight']");
        await Expect(highlight).ToBeVisibleAsync();
        await Expect(highlight.Locator("mark")).ToHaveTextAsync(["SQLite", "fast"]);

        // The private-use markers are consumed, never shown as tofu boxes.
        var text = await highlight.InnerTextAsync();
        Assert.DoesNotContain('', text);
        Assert.DoesNotContain('', text);
        Assert.Contains("SQLite is small, and fast", text, StringComparison.Ordinal);
    });

    [Fact]
    public Task A_chart_draws_its_series_and_shows_a_months_values_on_hover() => RunAsync(async () =>
    {
        await OpenAsync();

        var chart = Page.GetByRole(AriaRole.Figure, new PageGetByRoleOptions { Name = "Revenue and costs by month" });
        await chart.ScrollIntoViewIfNeededAsync();
        await Expect(chart).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        // The plot stretched to the box it was given, and a line was drawn with a real colour — a stroke class the
        // sheet never compiled would leave the path there and invisible.
        var plot = chart.Locator("svg");
        var box = (await plot.BoundingBoxAsync())!;
        var boxes = await chart.EvaluateAsync<string>(
            "f => [f, f.children[1], f.children[1].children[1], f.querySelector('svg')]"
            + ".map(e => { const r = e.getBoundingClientRect(); const s = getComputedStyle(e); "
            + "return e.tagName + '.' + e.getAttribute('class') + ' ' + Math.round(r.width) + 'x' + Math.round(r.height) "
            + "+ ' display=' + s.display + ' grow=' + s.flexGrow + ' pos=' + s.position; }).join(' | ')");
        Assert.True(box.Height > 100 && box.Width > 200, $"the plot is {box.Width}x{box.Height}: {boxes}");
        var stroke = await chart.Locator("path.stroke-warning").EvaluateAsync<string>("p => getComputedStyle(p).stroke");
        Assert.NotEqual("none", stroke);

        // Hover a month: its values appear, in CSS.
        var columns = chart.Locator(".group");
        await Expect(columns).ToHaveCountAsync(6);
        var april = columns.Nth(3);

        await april.HoverAsync();

        await Expect(april.Locator("div.group-hover\\:block").Last).ToBeVisibleAsync();
        await Expect(april).ToContainTextAsync("Apr");

        // And the numbers are there for a screen reader, one row per month.
        await Expect(chart.Locator("table.sr-only tbody tr")).ToHaveCountAsync(6);
    });

    private async Task OpenAsync()
    {
        await Page.GotoAsync(Docs);
        await Expect(Page.Locator(".side-nav a.side-nav-link[aria-current='page']").First).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });

        await ClickSidebar("Data display");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Data display",
            new LocatorAssertionsToContainTextOptions { Timeout = 15_000 });
    }
}
