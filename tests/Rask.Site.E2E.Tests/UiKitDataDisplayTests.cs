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
                     "ui-badge", "ui-card", "ui-kanban", "ui-accordion", "ui-aura", "ui-text-rotate", "ui-hover-3d",
                     "ui-hover-gallery", "ui-console-pieces", "ui-chart", "ui-table", "ui-display-rest",
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
    public Task A_badge_is_drawn_as_Flux_draws_it() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-badge']");
        var lime = scope.Locator("[data-ui-badge]").First;

        // Every example on Flux's page: 1 + 3 sizes + 3 icons + 1 rounded + 1 button + 3 removable
        // + 18 colours + 18 solid + 1 inset.
        await Expect(scope.Locator("[data-ui-badge]")).ToHaveCountAsync(49);
        await Expect(lime).ToHaveTextAsync("New");
        var drawn = await lime.EvaluateAsync<string[]>(
            "el => { const s = getComputedStyle(el); return [s.borderTopLeftRadius, s.paddingLeft, s.paddingTop, s.fontSize, s.fontWeight, s.height]; }");
        Assert.Equal(["6px", "8px", "4px", "14px", "500", "28px"], drawn);
        var fill = await lime.EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        Assert.Contains("0.25", fill, StringComparison.Ordinal);
    });

    [Fact]
    public Task A_badge_as_a_button_is_pressed_and_a_removable_one_is_removed() => RunAsync(async () =>
    {
        await OpenAsync();

        var scope = Page.Locator("[data-testid='ui-badge']");
        var amount = scope.Locator("button[data-testid='ui-badge-amount']");

        await Expect(amount).ToHaveTextAsync("Amount 1");
        await amount.ClickAsync();
        await Expect(amount).ToHaveTextAsync("Amount 2");

        await Expect(scope.Locator("[data-ui-badge-close]")).ToHaveCountAsync(3);
        await scope.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Remove Editor" }).ClickAsync();
        await Expect(scope.Locator("[data-ui-badge-close]")).ToHaveCountAsync(2);
        await Expect(scope.GetByText("Editor")).ToHaveCountAsync(0);
    });

    [Fact]
    public Task A_badge_set_into_a_heading_does_not_make_its_line_taller() => RunAsync(async () =>
    {
        await OpenAsync();

        var heading = Page.Locator("[data-testid='ui-badge-inset'] > div");
        var badge = heading.Locator("[data-ui-badge]");

        // 28px of badge in a 24px line: the inset gives the padding back as a negative margin.
        var heights = await heading.EvaluateAsync<double[]>(
            "el => [el.getBoundingClientRect().height, el.querySelector('[data-ui-badge]').getBoundingClientRect().height]");
        await Expect(badge).ToHaveTextAsync("New");
        Assert.Equal(24, heights[0]);
        Assert.Equal(28, heights[1]);
    });

    [Fact]
    public Task An_accordion_item_opens_with_a_click_and_with_the_keyboard_and_no_handler() => RunAsync(async () =>
    {
        await OpenAsync();
        var scope = Page.Locator("[data-testid='ui-accordion-basic']");
        var heading = Heading(scope, "What's your refund policy?");
        var content = scope.GetByText("30-day money-back guarantee");
        await Expect(content).ToBeHiddenAsync();

        await heading.ClickAsync();
        await Expect(content).ToBeVisibleAsync();
        await heading.PressAsync("Enter");
        await Expect(content).ToBeHiddenAsync();
        await heading.PressAsync("Space");

        // No handler anywhere on it: the browser did all three.
        await Expect(content).ToBeVisibleAsync();
        Assert.Null(await scope.Locator("details").First.GetAttributeAsync("data-rask-on-toggle"));
        Assert.Equal("rgb(39, 39, 42)", await ShownChevron(heading).EvaluateAsync<string>(InSrgb));
    });

    [Fact]
    public Task An_exclusive_accordion_keeps_one_item_open() => RunAsync(async () =>
    {
        await OpenAsync();
        var scope = Page.Locator("[data-testid='ui-accordion-exclusive']");
        var refund = scope.GetByText("30-day money-back guarantee");
        var bulk = scope.GetByText("special discounts for bulk orders");

        await Heading(scope, "What's your refund policy?").ClickAsync();
        await Expect(refund).ToBeVisibleAsync();
        await Heading(scope, "Do you offer any discounts").ClickAsync();

        await Expect(bulk).ToBeVisibleAsync();
        await Expect(refund).ToBeHiddenAsync();
        await Expect(scope.Locator("details[open]")).ToHaveCountAsync(1);
    });

    [Fact]
    public Task A_disabled_item_cannot_be_opened_and_Tab_passes_over_it() => RunAsync(async () =>
    {
        await OpenAsync();
        var scope = Page.Locator("[data-testid='ui-accordion-disabled']");
        var disabled = Heading(scope, "Do you offer PPP discounts?");

        await disabled.ClickAsync(new LocatorClickOptions { Force = true });
        await Heading(scope, "What's your refund policy?").FocusAsync();
        await Page.Keyboard.PressAsync("Tab");

        await Expect(scope.Locator("details[open]")).ToHaveCountAsync(0);
        await Expect(Heading(scope, "How do I track my order?")).ToBeFocusedAsync();
        await Expect(disabled).ToHaveAttributeAsync("aria-disabled", "true");
        Assert.Equal("rgb(159, 159, 169)", await disabled.EvaluateAsync<string>(InSrgb));
    });

    [Fact]
    public Task The_expanded_item_is_open_as_the_page_loads_and_a_reversed_chevron_leads_its_heading() => RunAsync(async () =>
    {
        await OpenAsync();
        var expanded = Page.Locator("[data-testid='ui-accordion-expanded']");
        var reversed = Heading(Page.Locator("[data-testid='ui-accordion-reverse']"), "What's your refund policy?");

        var chevron = (await ShownChevron(reversed).BoundingBoxAsync())!;
        var text = (await reversed.Locator("span").BoundingBoxAsync())!;

        await Expect(expanded.GetByText("special discounts for bulk orders")).ToBeVisibleAsync();
        await Expect(expanded.Locator("details[open]")).ToHaveCountAsync(1);
        Assert.Equal(text.X, chevron.X + chevron.Width + 8, 1);
    });

    [Fact]
    public Task A_transitioning_accordion_opens_over_a_quarter_of_a_second() => RunAsync(async () =>
    {
        await OpenAsync();
        var item = Page.Locator("[data-testid='ui-accordion-transition'] details").First;
        var closed = (await item.BoundingBoxAsync())!.Height;

        var slot = await item.EvaluateAsync<string>(
            "d => { const s = getComputedStyle(d, '::details-content'); return s.transitionProperty + ' | ' + s.transitionDuration + ' | ' + s.transitionTimingFunction; }");
        // Clicked and timed in the page: a click sent from here would spend the 100ms on the way.
        var midway = await item.EvaluateAsync<double>(
            "d => new Promise(done => { d.querySelector('summary').click(); setTimeout(() => done(d.getBoundingClientRect().height), 100); })");
        await Expect(item.GetByText("30-day money-back guarantee")).ToBeVisibleAsync();
        await Page.WaitForTimeoutAsync(300);
        var open = (await item.BoundingBoxAsync())!.Height;

        Assert.StartsWith("height, content-visibility, overflow | 0.25s, 0.25s, 0s | cubic-bezier(0.4, 0, 0.2, 1)", slot, StringComparison.Ordinal);
        Assert.True(midway > closed + 1 && midway < open - 1, $"100ms in, the item was {midway}px between {closed}px and {open}px");
    });

    // TheRotatorShowsEveryWordInTheMarkup moved DOWN to Rask.UiTests.Components.UiTextRotateTests.
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
    public Task A_card_draws_its_parts_as_Flux_does() => RunAsync(async () =>
    {
        await OpenAsync();

        var profile = Page.Locator("[data-testid='ui-card'] [data-ui-card][data-ui-card-body-variant='inset'][data-ui-card-size='lg']");
        var body = profile.Locator("> [data-ui-card-body]");

        // The inset body is its own panel: four pixels in from the card, with corners four pixels tighter.
        await Expect(profile).ToHaveCSSAsync("border-radius", "16px");
        await Expect(profile).ToHaveCSSAsync("padding", "4px");
        await Expect(body).ToHaveCSSAsync("border-radius", "12px");
        await Expect(profile.Locator("> [data-ui-card-header] [data-ui-card-heading]")).ToHaveTextAsync("Profile");
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

            var badge = Page.Locator("[data-testid='ui-console-pieces'] [data-ui-badge].font-mono");
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

    [Fact]
    public Task A_sortable_table_heading_sorts_the_rows_and_turns_round_on_a_second_click() => RunAsync(async () =>
    {
        await OpenAsync();

        var table = Page.Locator("#ui-orders");
        var amount = table.Locator("th").Filter(new LocatorFilterOptions { HasText = "Amount" });
        var firstAmount = table.Locator("tbody tr").First.Locator("td").Last;
        await table.ScrollIntoViewIfNeededAsync();

        // Sorted by date at first, so the Amount heading keeps its chevron hidden until it is hovered.
        await Expect(amount.Locator("button div div")).ToHaveCSSAsync("opacity", "0");

        await amount.GetByRole(AriaRole.Button).ClickAsync();

        await Expect(firstAmount).ToHaveTextAsync("$12.00");

        // The cell is the target, not only the button inside it: Flux's click lands on the heading.
        await amount.ClickAsync(new LocatorClickOptions { Position = new Position { X = 2, Y = 2 } });

        await Expect(firstAmount).ToHaveTextAsync("$313.00");
        await Expect(table.Locator("tbody tr")).ToHaveCountAsync(4);
    });

    [Fact]
    public Task A_sticky_table_column_stays_put_and_casts_its_shadow_only_once_scrolled() => RunAsync(async () =>
    {
        await OpenAsync();

        var area = Page.Locator("[data-testid='ui-table'] div:has(> table[data-ui-table])").Last;
        var id = area.Locator("tbody td").First;
        await area.ScrollIntoViewIfNeededAsync();
        const string shadow = "el => getComputedStyle(el, '::after').boxShadow";
        Assert.Equal("none", await id.EvaluateAsync<string>(shadow));

        await area.EvaluateAsync("el => el.scrollTo(60, 80)");

        // Scroll-driven, so it needs a frame to catch up.
        await Page.WaitForFunctionAsync(
            "el => getComputedStyle(el, '::after').boxShadow !== 'none'", await id.ElementHandleAsync());
        var offset = await id.EvaluateAsync<double>(
            "el => el.getBoundingClientRect().left - el.closest('table').parentElement.getBoundingClientRect().left");
        Assert.InRange(offset, -0.5, 0.5);
        var head = await area.Locator("thead").EvaluateAsync<double>(
            "el => el.getBoundingClientRect().top - el.closest('table').parentElement.getBoundingClientRect().top");
        Assert.InRange(head, -0.5, 0.5);
    });

    [Fact]
    public Task A_kanban_board_is_drawn_as_Flux_draws_it() => RunAsync(async () =>
    {
        await OpenAsync();

        var board = Page.Locator("[data-testid='ui-kanban-board']");
        var columns = board.Locator("[data-ui-kanban-column]");
        var card = board.Locator("[ui-kanban-card]").First;

        // Flux's numbers: 320px columns 16px apart, a 44px card with 12px of padding and 8px corners.
        await Expect(columns).ToHaveCountAsync(3);
        await Expect(board.Locator("[ui-kanban-card]")).ToHaveCountAsync(9);
        var lefts = await columns.EvaluateAllAsync<double[]>("els => els.map(el => el.getBoundingClientRect().left - els[0].getBoundingClientRect().left)");
        Assert.Equal([0, 336, 672], lefts);
        var drawn = await card.EvaluateAsync<string[]>(
            "el => { const s = getComputedStyle(el); return [s.paddingLeft, s.borderTopLeftRadius, `${el.getBoundingClientRect().width}x${el.getBoundingClientRect().height}`]; }");
        Assert.Equal(["12px", "8px", "304x44"], drawn);
        var header = await board.Locator("[data-ui-kanban-column-header]").First.EvaluateAsync<double>("el => el.getBoundingClientRect().height");
        Assert.Equal(48, header);
        // A board draws; it moves nothing. Flux's has no draggable card either.
        await Expect(Page.Locator("[data-testid='ui-kanban'] [draggable='true']")).ToHaveCountAsync(0);
    });

    [Fact]
    public Task A_kanban_card_as_a_button_opens_with_the_pointer_and_with_the_keyboard() => RunAsync(async () =>
    {
        await OpenAsync();

        var cards = Page.Locator("[data-testid='ui-kanban-buttons'] button[data-ui-kanban-card]");
        var opened = Page.Locator("[data-testid='ui-kanban-opened']");

        await Expect(cards).ToHaveCountAsync(3);
        await Expect(opened).ToHaveTextAsync("No card opened yet.");
        await cards.Nth(0).ClickAsync();
        await Expect(opened).ToHaveTextAsync("Opened: Update privacy policy in app");
        await cards.Nth(1).FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(opened).ToHaveTextAsync("Opened: Search bar suggestions broken");
        await cards.Nth(2).FocusAsync();
        await Page.Keyboard.PressAsync("Space");
        await Expect(opened).ToHaveTextAsync("Opened: Improve loading spinner visuals");
        // The header slot's badges sit above the heading, the footer slot's icon under it.
        var order = await cards.Nth(1).EvaluateAsync<bool>(
            "el => el.querySelector('[data-ui-badge]').getBoundingClientRect().bottom <= el.querySelector('[data-ui-heading]').getBoundingClientRect().top");
        Assert.True(order);
        var under = await cards.Nth(2).EvaluateAsync<bool>(
            "el => el.querySelector('svg').getBoundingClientRect().top >= el.querySelector('[data-ui-heading]').getBoundingClientRect().bottom");
        Assert.True(under);
    });

    [Fact]
    public Task A_kanban_column_footer_adds_a_card_and_its_count_follows() => RunAsync(async () =>
    {
        await OpenAsync();

        var column = Page.Locator("[data-testid='ui-kanban-footer']");
        var cards = column.Locator("[data-ui-kanban-column-cards] [ui-kanban-card]");
        var count = column.Locator("[data-ui-kanban-column-header] [data-ui-heading] + div");

        await Expect(cards).ToHaveCountAsync(2);
        await Expect(count).ToHaveTextAsync("2");
        await column.GetByPlaceholder("New card...").FillAsync("Write the release notes");
        await column.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Add", Exact = true }).ClickAsync();
        await Expect(cards).ToHaveCountAsync(3);
        await Expect(cards.Nth(2)).ToHaveTextAsync("Write the release notes");
        await Expect(count).ToHaveTextAsync("3");
        // The other column over the same list has the card too: the board is the page's own state, drawn.
        await Expect(Page.Locator("[data-testid='ui-kanban-actions'] [ui-kanban-card]")).ToHaveCountAsync(3);
    });

    // A computed colour in sRGB whatever colour space the sheet states it in.
    private const string InSrgb =
        "el => { const c = document.createElement('canvas').getContext('2d'); c.fillStyle = getComputedStyle(el).color; "
        + "c.fillRect(0, 0, 1, 1); const [r, g, b] = c.getImageData(0, 0, 1, 1).data; return `rgb(${r}, ${g}, ${b})`; }";

    private static ILocator Heading(ILocator scope, string text) =>
        scope.Locator("summary").Filter(new LocatorFilterOptions { HasText = text });

    private static ILocator ShownChevron(ILocator heading) => heading.Locator("svg:visible");

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
