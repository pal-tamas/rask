using System.Globalization;
using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     A long list of options draws only the rows its scroll shows (<c>data-rask-list-window</c>): a select, a combobox and
///     a pillbox over the rows of a table — TA's 1098 vehicles, 873 drivers — scrolled through without typing.
/// </summary>
/// <remarks>
///     The browser's part — the box that scrolls, the keys that walk past the rows drawn — is played here by the events it
///     sends: a scroll saying where the box is, what is typed into the search field, the letters type-ahead writes into the
///     seek field. The keys themselves are walked in a browser (<c>UiKitSelectWindowTests</c>).
/// </remarks>
public partial class UiSelectWindowTests : global::Rask.Core.RaskMarkup
{
    private const string Box = "[data-rask-list-window]";

    private const string Seek = "[data-rask-list-seek]";

    private const string Field = "[data-ui-select-search] input";

    private int? _picked;

    private List<int> _several = [];

    private static string NameOf(int id) => id == 900 ? "Zeppelin" : "Vehicle " + id.ToString("0000", CultureInfo.InvariantCulture);

    private static IEnumerable<Component> Options(int count) =>
        Enumerable.Range(0, count).Select(id => Ui.SelectOption.Key(id).Value(id)[NameOf(id)]);

    private Component Listbox(int count) =>
        Ui.Select.Value(_picked).OnChange(value => _picked = value).Listbox.Placeholder("Choose a vehicle…")[Options(count)];

    private Component Searchable(int count) =>
        Ui.Select.Value(_picked).OnChange(value => _picked = value).Listbox.Searchable()[Options(count)];

    private static string[] Rows(Page page) => [.. page.FindAll("[role=\"option\"]").Select(row => row.TextContent.Trim())];

    private static int[] Places(Page page) =>
        [.. page.FindAll("[role=\"option\"]").Select(row => int.Parse(row.Attribute("aria-posinset")!, CultureInfo.InvariantCulture))];

    // What a search field sends once typing has paused (rask-input.ts counts the pause in the browser).
    private static Task Pause(Page page, string field, string text) =>
        page.On(field).Raise("pause", "{\"value\":" + System.Text.Json.JsonSerializer.Serialize(text) + "}");

    private static Task Scroll(Page page, int top) =>
        page.On(Box).Raise("scroll", "{\"target\":{\"scrollTop\":" + top.ToString(CultureInfo.InvariantCulture) + ",\"clientHeight\":320,\"scrollHeight\":32000}}");

    [Fact]
    public void A_list_of_a_hundred_options_is_drawn_whole_as_Flux_draws_it()
    {
        var page = Acting.Render(() => Listbox(100));

        var rows = Rows(page);

        Assert.Equal(100, rows.Length);
        Assert.Empty(page.FindAll(Box));
        Assert.Empty(page.FindAll("[aria-setsize]"));
        Assert.Empty(page.FindAll(Seek));
    }

    [Fact]
    public void A_list_of_a_thousand_options_draws_only_the_rows_its_box_shows()
    {
        var page = Acting.Render(() => Listbox(1000));

        var rows = Rows(page);

        Assert.Equal(20, rows.Length);
        Assert.Equal("Vehicle 0000", rows[0]);
        Assert.Equal("Vehicle 0019", rows[^1]);
        Assert.Equal("32 1 1000", page.Find(Box).Attribute(UiWindowMarks.Window));
        Assert.Equal("height:0px", page.FindAll("[data-rask-list-window] > [aria-hidden]")[0].Attribute("style"));
        Assert.Equal("height:" + (980 * 32).ToString(CultureInfo.InvariantCulture) + "px", page.FindAll("[data-rask-list-window] > [aria-hidden]")[1].Attribute("style"));
    }

    [Fact]
    public void Every_row_drawn_says_how_many_rows_there_are_and_which_it_is()
    {
        var page = Acting.Render(() => Listbox(1000));

        var rows = page.FindAll("[role=\"option\"]");

        Assert.All(rows, row => Assert.Equal("1000", row.Attribute("aria-setsize")));
        Assert.Equal(Enumerable.Range(1, 20), Places(page));
        Assert.All(rows, row => Assert.Equal("height:32px", row.Attribute("style")));
    }

    [Fact]
    public async Task A_scroll_draws_the_rows_the_box_shows_there()
    {
        var page = Acting.Render(() => Listbox(1000));

        await Scroll(page, 500 * 32);

        Assert.Equal(Enumerable.Range(491, 30), Places(page));
        Assert.Equal("Vehicle 0490", Rows(page)[0]);
        Assert.Equal("height:" + (490 * 32).ToString(CultureInfo.InvariantCulture) + "px", page.FindAll("[data-rask-list-window] > [aria-hidden]")[0].Attribute("style"));
    }

    [Fact]
    public async Task Scrolled_to_its_end_the_list_draws_its_last_row_and_picks_it()
    {
        var page = Acting.Render(() => Listbox(1000));

        await Scroll(page, (1000 * 32) - 320);
        await page.On("#" + page.FindAll("[role=\"option\"]")[^1].Attribute("id")).Click();

        Assert.Equal(999, _picked);
        Assert.Equal("Vehicle 0999", page.TextOf("[data-ui-select-button]"));
    }

    [Fact]
    public async Task What_is_typed_into_the_search_narrows_the_whole_list_not_the_rows_drawn()
    {
        var page = Acting.Render(() => Searchable(1000));

        await Pause(page, Field, "vehicle 09");

        Assert.Equal("99", page.FindAll("[role=\"option\"]")[0].Attribute("aria-setsize"));
        Assert.Equal("Vehicle 0901", Rows(page)[0]);
        Assert.Equal("32 2 99", page.Find(Box).Attribute(UiWindowMarks.Window));
    }

    [Fact]
    public void On_a_server_the_search_of_a_windowed_list_waits_a_tenth_of_a_second_for_typing_to_pause()
    {
        var page = Acting.Render(() => Searchable(1000));

        var field = page.Find(Field);

        Assert.Equal("100", field.Attribute("data-rask-debounce"));
        Assert.NotNull(field.Attribute("data-rask-on-pause"));
        Assert.Null(field.Attribute("data-rask-on-input"));
    }

    [Fact]
    public void A_debounce_the_page_gives_the_search_wins_over_the_windowed_lists_own()
    {
        var page = Acting.Render(() =>
            Ui.Select.Value(_picked).OnChange(value => _picked = value).Listbox.Searchable()[
                Ui.SelectSearch.Debounce(400.Milliseconds),
                Options(1000)
            ]);

        var field = page.Find(Field);

        Assert.Equal("400", field.Attribute("data-rask-debounce"));
    }

    [Fact]
    public void A_list_of_a_hundred_options_still_hides_its_rows_in_the_browser_at_every_key()
    {
        var page = Acting.Render(() => Searchable(100));

        var field = page.Find(Field);

        Assert.Null(field.Attribute("data-rask-debounce"));
        Assert.Contains("filter", page.Find("[data-rask-list]").Attribute("data-rask-list")!.Split(' '));
    }

    [Fact]
    public async Task A_search_that_finds_nothing_says_so_and_a_new_one_starts_at_the_top()
    {
        var page = Acting.Render(() => Searchable(1000));
        await Scroll(page, 600 * 32);

        await Pause(page, Field, "nothing like it");

        Assert.Empty(Rows(page));
        Assert.Null(page.Find("[data-ui-listbox-empty]").Attribute("data-hidden"));
        await Pause(page, Field, "");
        Assert.Equal(Enumerable.Range(1, 20), Places(page));
    }

    [Fact]
    public void The_browser_does_not_hide_the_rows_of_a_windowed_list_and_holds_no_pick()
    {
        var page = Acting.Render(() => Searchable(1000));

        var words = page.Find("[data-rask-list]").Attribute("data-rask-list")!.Split(' ');

        Assert.DoesNotContain("filter", words);
        Assert.DoesNotContain("held", words);
    }

    [Fact]
    public async Task Type_ahead_in_an_open_list_draws_the_row_it_lands_on_past_the_window()
    {
        var page = Acting.Render(() => Listbox(1000));

        await page.On(Seek).Input("7\n1\n-1\nz\nz");

        Assert.Equal("7 900", page.Find(Box).Attribute("data-rask-list-sought"));
        Assert.Contains(901, Places(page));
        Assert.Contains("Zeppelin", Rows(page));
        Assert.Null(_picked);
    }

    [Fact]
    public async Task Type_ahead_on_a_shut_list_picks_the_row_it_lands_on()
    {
        var page = Acting.Render(() => Listbox(1000));

        await page.On(Seek).Input("0\n0\n-1\nze\ne");

        Assert.Equal(900, _picked);
        Assert.Equal("Zeppelin", page.TextOf("[data-ui-select-button]"));
    }

    [Fact]
    public async Task The_picked_row_says_where_it_is_so_the_list_opens_on_it()
    {
        _picked = 700;
        var page = Acting.Render(() => Listbox(1000));

        await Scroll(page, 700 * 32);

        Assert.Equal("700", page.Find(Box).Attribute("data-rask-list-picked"));
        Assert.Equal("true", page.Find("[aria-posinset=\"701\"]").Attribute("aria-selected"));
    }

    [Fact]
    public async Task A_row_picked_stays_said_by_the_button_once_it_is_scrolled_out_of_the_window()
    {
        var page = Acting.Render(() => Listbox(1000));
        await page.On("[aria-posinset=\"5\"]").Click();

        await Scroll(page, 800 * 32);

        Assert.Equal(4, _picked);
        Assert.DoesNotContain("Vehicle 0004", Rows(page));
        Assert.Equal("Vehicle 0004", page.TextOf("[data-ui-select-button]"));
    }

    [Fact]
    public void A_pillbox_of_a_thousand_options_is_windowed_too()
    {
        var page = Acting.Render(() =>
            Ui.Pillbox.Values(_several).OnChange(values => _several = [.. values])[
                Enumerable.Range(0, 1000).Select(id => Ui.PillboxOption.Key(id).Value(id)[NameOf(id)])
            ]);

        var rows = page.FindAll("[data-ui-listbox-option]");

        Assert.Equal(20, rows.Count);
        Assert.Equal("1000", rows[0].Attribute("aria-setsize"));
    }

    [Fact]
    public async Task A_combobox_narrows_its_window_by_what_is_typed_and_not_by_its_answer()
    {
        _picked = 3;
        var page = Acting.Render(() => Ui.Select.Value(_picked).OnChange(value => _picked = value).Combobox[Options(1000)]);

        await Pause(page, "input[role=\"combobox\"]", "Vehicle 0003");
        var said = page.Find(Box).Attribute(UiWindowMarks.Window);
        await Pause(page, "input[role=\"combobox\"]", "eppel");

        Assert.Equal("1000", said!.Split(' ')[2]);
        Assert.Equal(["Zeppelin"], Rows(page));
    }

    [Fact]
    public void Choices_of_more_than_a_hundred_rows_are_windowed()
    {
        var vehicles = Enumerable.Range(0, 1098).Select(id => new Ui.Choice<int>(id, NameOf(id))).ToArray();

        var page = Acting.Render(() => Ui.Select.Value(_picked).OnChange(value => _picked = value).Choices(vehicles));

        Assert.Equal("1098", page.FindAll("[role=\"option\"]")[0].Attribute("aria-setsize"));
        Assert.True(Rows(page).Length <= 30);
    }
}

/// <summary>What a windowed list says of itself, by name.</summary>
internal static class UiWindowMarks
{
    internal const string Window = "data-rask-list-window";
}
