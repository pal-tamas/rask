using System.Globalization;
using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     <c>.Choices((typed, page) =&gt; …)</c> on <c>Ui.Select</c> and <c>Ui.Pillbox</c> (#1354): a searched source that pages
///     as the list scrolls — Eszaf's product picker over every product, where the one-list search showed its first twenty and
///     no more.
/// </summary>
/// <remarks>
///     The browser's part is played by the events it sends: a scroll saying where the box is, what the search field says
///     once typing has paused. The wheel and the keys are walked in a browser (<c>PagedChoicesScrollTests</c>).
/// </remarks>
[Collection(AskedSearchCollection.Name)]
public partial class UiChoicePagesTests : global::Rask.Core.RaskMarkup
{
    private const string Box = "[data-rask-list-window]";

    private const string Field = "[data-ui-select-search] input";

    private const string PillField = "[data-ui-pillbox-search] input";

    private readonly List<(string Typed, int Skip, int Take)> _asked = [];

    private sealed class Order
    {
        public int? ProductId { get; set; }

        public ICollection<int> ProductIds { get; set; } = [];
    }

    private static string NameOf(int id) => "Product " + id.ToString("000", CultureInfo.InvariantCulture);

    // A store of `count` products in the order of their ids: the page of those whose name holds what was typed.
    private Func<string, Ui.ChoicePage, Task<IReadOnlyList<Ui.Choice<int>>>> Store(int count) => (typed, page) =>
    {
        _asked.Add((typed, page.Skip, page.Take));
        return Task.FromResult<IReadOnlyList<Ui.Choice<int>>>(
            [.. Matching(count, typed).Skip(page.Skip).Take(page.Take)]);
    };

    private static IEnumerable<Ui.Choice<int>> Matching(int count, string typed) =>
        Enumerable.Range(1, count).Select(id => new Ui.Choice<int>(id, NameOf(id)))
            .Where(row => row.Name.Contains(typed, StringComparison.OrdinalIgnoreCase));

    private static string[] Rows(Page page) =>
        [.. page.FindAll("[role=\"option\"]").Select(row => row.Attribute("data-rask-list-text") ?? row.TextContent.Trim())];

    // What the box says: row height, which set of rows, how many are on offer.
    private static string Window(Page page) => page.Find(Box).Attribute(UiWindowMarks.Window)!;

    // The box scrolled so `row` is its top row.
    private static Task Scroll(Page page, int row) =>
        page.On(Box).Raise(
            "scroll",
            "{\"target\":{\"scrollTop\":" + (row * 32).ToString(CultureInfo.InvariantCulture) + ",\"clientHeight\":320,\"scrollHeight\":64000}}");

    private static Task Pause(Page page, string text, string field = Field) =>
        page.On(field).Raise("pause", "{\"value\":" + System.Text.Json.JsonSerializer.Serialize(text) + "}");

    // The same store render after render, as a method group is: one handed anew is another search, asked again.
    private Page Select(Order order, int count, Ui.Choice<int>? held = null)
    {
        var store = Store(count);
        return Acting.Render(() => Ui.Select.Bind(() => order.ProductId).Live().Choices(store, held: held));
    }

    private Page Pillbox(Order order, int count)
    {
        var store = Store(count);
        return Acting.Render(() => Ui.Pillbox.Bind(() => order.ProductIds).Live().Choices(store));
    }

    [Fact]
    public void The_list_opens_on_the_first_page_for_nothing_typed_and_draws_it_windowed()
    {
        var order = new Order();

        var page = Select(order, 500);

        Assert.Equal([("", 0, 50)], _asked);
        Assert.Equal("32 1 50", Window(page));
        Assert.Equal(NameOf(1), Rows(page)[0]);
        Assert.All(page.FindAll("[role=\"option\"]"), row => Assert.Equal("-1", row.Attribute("aria-setsize")));
    }

    [Fact]
    public async Task Scrolling_near_the_end_of_the_rows_found_asks_for_the_next_page_and_puts_it_after_them()
    {
        var order = new Order();
        var page = Select(order, 500);

        await Scroll(page, 30);
        await Scroll(page, 60);

        Assert.Equal([("", 0, 50), ("", 50, 50)], _asked);
        Assert.Equal("32 1 100", Window(page));
        Assert.Equal(NameOf(51), Rows(page)[0]);
    }

    [Fact]
    public async Task A_scroll_that_stays_away_from_the_end_asks_for_nothing_more()
    {
        var order = new Order();
        var page = Select(order, 500);

        await Scroll(page, 10);

        Assert.Equal([("", 0, 50)], _asked);
        Assert.Equal("32 1 50", Window(page));
    }

    [Fact]
    public async Task A_page_of_fewer_rows_than_asked_for_is_the_end_and_nothing_past_it_is_asked_for()
    {
        var order = new Order();
        var page = Select(order, 120);
        await Scroll(page, 30);
        await Scroll(page, 80);

        await Scroll(page, 110);

        Assert.Equal([("", 0, 50), ("", 50, 50), ("", 100, 50)], _asked);
        Assert.Equal("32 1 120", Window(page));
        Assert.Equal(NameOf(120), Rows(page)[^1]);
        Assert.All(page.FindAll("[role=\"option\"]"), row => Assert.Equal("120", row.Attribute("aria-setsize")));
    }

    [Fact]
    public async Task Typing_starts_again_from_the_first_page_of_what_was_typed()
    {
        var order = new Order();
        var page = Select(order, 500);
        await Scroll(page, 30);

        await Pause(page, " product 4 ");

        Assert.Equal(("product 4", 0, 50), _asked[^1]);
        Assert.Equal("32 2 50", Window(page));
        Assert.Equal(NameOf(400), Rows(page)[0]);
    }

    [Fact]
    public async Task A_scroll_after_typing_pages_through_what_was_typed()
    {
        var order = new Order();
        var page = Select(order, 500);
        await Pause(page, "product 4");

        await Scroll(page, 30);

        Assert.Equal(("product 4", 50, 50), _asked[^1]);
        Assert.Equal("32 2 100", Window(page));
    }

    [Fact]
    public async Task A_page_asked_for_older_words_is_dropped_once_something_else_is_typed()
    {
        var order = new Order();
        var held = new TaskCompletionSource<IReadOnlyList<Ui.Choice<int>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<string, Ui.ChoicePage, Task<IReadOnlyList<Ui.Choice<int>>>> store = (typed, at) => at.Skip > 0
            ? held.Task
            : Task.FromResult<IReadOnlyList<Ui.Choice<int>>>([.. Matching(500, typed).Take(at.Take)]);
        var page = Acting.Render(() => Ui.Select.Bind(() => order.ProductId).Live().Choices(store));
        var scrolled = Scroll(page, 30);

        await Pause(page, "product 49");
        held.SetResult([.. Matching(500, string.Empty).Skip(50).Take(50)]);
        await scrolled.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal("32 2 10", Window(page));
        Assert.DoesNotContain(NameOf(51), Rows(page));
        Assert.Equal("10", page.FindAll("[role=\"option\"]")[0].Attribute("aria-setsize"));
    }

    [Fact]
    public void A_held_row_stays_first_while_the_pages_found_do_not_bring_it()
    {
        var order = new Order { ProductId = 450 };

        var page = Select(order, 500, held: new Ui.Choice<int>(450, NameOf(450)));

        Assert.Equal(NameOf(450), Rows(page)[0]);
        Assert.Equal(NameOf(1), Rows(page)[1]);
        Assert.Equal(NameOf(450), page.TextOf("[data-ui-select-button]"));
    }

    [Fact]
    public async Task A_row_of_the_third_page_is_picked_into_the_pillbox()
    {
        var order = new Order();
        var page = Pillbox(order, 500);
        await Scroll(page, 30);
        await Scroll(page, 80);
        await Scroll(page, 120);

        await page.On("#" + page.FindAll("[role=\"option\"]").Single(row => row.TextContent.Trim() == NameOf(125)).Attribute("id")).Click();

        Assert.Equal(("", 100, 50), _asked[2]);
        Assert.Equal([125], order.ProductIds);
        Assert.Contains(NameOf(125), page.Find("[data-ui-pillbox-trigger]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pillbox_pages_through_what_its_search_field_says()
    {
        var order = new Order();
        var page = Pillbox(order, 500);

        await Pause(page, "product 1", PillField);
        await Scroll(page, 30);

        Assert.Equal([("", 0, 50), ("product 1", 0, 50), ("product 1", 50, 50)], _asked);
        Assert.Equal("32 2 100", Window(page));
    }

    [Fact]
    public void The_one_list_search_is_asked_once_and_draws_every_row_it_answers_whole()
    {
        var order = new Order();
        var asked = new List<string>();

        var page = Acting.Render(() => Ui.Select.Bind(() => order.ProductId).Live().Choices(typed =>
        {
            asked.Add(typed);
            return Task.FromResult<IReadOnlyList<Ui.Choice<int>>>([.. Matching(500, typed).Take(20)]);
        }));

        Assert.Equal([""], asked);
        Assert.Empty(page.FindAll(Box));
        Assert.Equal(20, Rows(page).Length);
    }
}
