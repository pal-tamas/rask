using System.Globalization;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     A paging source that says how many rows it has (<c>Ui.ChoiceRows</c>, owner 2026-11-20): the list is as long as all
///     of them from its first page on, so its scrollbar is the whole list's, and a scroll anywhere is answered at once.
/// </summary>
public partial class UiChoicePagesTests
{
    // The store of `count` products, saying how many match what was typed.
    private Func<string, Ui.ChoicePage, Task<IReadOnlyList<Ui.Choice<int>>>> Counted(int count) => (typed, page) =>
    {
        _asked.Add((typed, page.Skip, page.Take));
        var matching = Matching(count, typed).ToList();
        return Task.FromResult<IReadOnlyList<Ui.Choice<int>>>(
            new Ui.ChoiceRows<int>([.. matching.Skip(page.Skip).Take(page.Take)], matching.Count));
    };

    private Page CountedSelect(Order order, int count)
    {
        var store = Counted(count);
        return Acting.Render(() => Ui.Select.Bind(() => order.ProductId).Live().Choices(store));
    }

    // The spacers' heights and the rows drawn between them, in pixels: what the box scrolls through.
    private static int Tall(Page page) =>
        page.FindAll("[data-ui-list-unseen]").Sum(spacer => Pixels(spacer.Attribute("style")!))
        + page.FindAll("[role=\"option\"]").Sum(row => Pixels(row.Attribute("style")!));

    private static int Pixels(string style) =>
        int.Parse(style.Split(';')[0]["height:".Length..^"px".Length], CultureInfo.InvariantCulture);

    [Fact]
    public void A_source_that_says_how_many_rows_it_has_is_drawn_that_long_from_its_first_page()
    {
        var order = new Order();

        var page = CountedSelect(order, 500);

        Assert.Equal([("", 0, 50)], _asked);
        Assert.Equal("32 1 500", Window(page));
        Assert.Equal(500 * 32, Tall(page));
        Assert.All(page.FindAll("[role=\"option\"]"), row => Assert.Equal("500", row.Attribute("aria-setsize")));
    }

    [Fact]
    public async Task A_scroll_far_past_the_rows_found_asks_once_for_every_row_up_to_there()
    {
        var order = new Order();
        var page = CountedSelect(order, 500);

        await Scroll(page, 400);

        Assert.Equal([("", 0, 50), ("", 50, 400)], _asked);
        Assert.Equal(NameOf(391), Rows(page)[0]);
        Assert.Equal(500 * 32, Tall(page));
    }

    [Fact]
    public async Task The_scrollbar_stays_as_long_as_the_whole_list_as_the_pages_arrive()
    {
        var order = new Order();
        var page = CountedSelect(order, 500);

        await Scroll(page, 30);

        Assert.Equal([("", 0, 50), ("", 50, 50)], _asked);
        Assert.Equal("32 1 500", Window(page));
        Assert.Equal(500 * 32, Tall(page));
    }

    [Fact]
    public async Task Once_the_last_row_the_source_has_is_here_nothing_more_is_asked_for()
    {
        var order = new Order();
        var page = CountedSelect(order, 120);
        await Scroll(page, 110);

        await Scroll(page, 0);
        await Scroll(page, 110);

        Assert.Equal([("", 0, 50), ("", 50, 100)], _asked);
        Assert.Equal("32 1 120", Window(page));
        Assert.Equal(NameOf(120), Rows(page)[^1]);
        Assert.Equal(120 * 32, Tall(page));
    }

    [Fact]
    public async Task A_count_of_what_was_typed_is_the_lists_length()
    {
        var order = new Order();
        var page = CountedSelect(order, 500);

        await Pause(page, "product 4");

        Assert.Equal("32 2 100", Window(page));
        Assert.Equal(100 * 32, Tall(page));
    }

    [Fact]
    public async Task A_source_that_does_not_say_has_a_box_of_rows_on_their_way_past_the_last_row_found()
    {
        var order = new Order();
        var page = Select(order, 500);

        Assert.Equal((50 + 10) * 32, Tall(page));

        await Scroll(page, 30);

        Assert.Equal([("", 0, 50), ("", 50, 50)], _asked);
        Assert.Equal("32 1 100", Window(page));
        Assert.Equal((100 + 10) * 32, Tall(page));
    }

    [Fact]
    public void A_spacer_of_rows_with_a_line_under_their_words_says_its_rows_are_tall()
    {
        var order = new Order();
        Func<string, Ui.ChoicePage, Task<IReadOnlyList<Ui.Choice<int>>>> store = (typed, at) => Task.FromResult<IReadOnlyList<Ui.Choice<int>>>(
            new Ui.ChoiceRows<int>([.. Enumerable.Range(at.Skip + 1, at.Take).Select(id => new Ui.Choice<int>(id, NameOf(id), "Detail"))], 500));

        var page = Acting.Render(() => Ui.Select.Bind(() => order.ProductId).Live().Choices(store));

        Assert.All(page.FindAll("[data-ui-list-unseen]"), spacer => Assert.Equal("tall", spacer.Attribute("data-ui-list-unseen")));
        Assert.Equal(500 * 60, Tall(page));
    }
}
