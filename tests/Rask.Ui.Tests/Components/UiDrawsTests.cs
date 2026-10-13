using System.Globalization;
using Rask.Core;
using Rask.UiTests.Flux;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     Which handlers of a kit control say they only draw it (<c>data-rask-draws</c>): the runtime sends those
///     alone, and nothing that waits for the reader's next action is written for them. A handler that reaches
///     a callback the app gave, or writes a value now, is never named.
/// </summary>
public partial class UiDrawsTests : global::Rask.Core.RaskMarkup
{
    private const string Mark = "data-rask-draws";

    private static readonly DateOnly Today = new(2026, 1, 15);

    private static readonly string[] Words = ["Alpha", "Beta"];

    private static IEnumerable<Component> Options() => Words.Select(word => Ui.SelectOption.Key(word).Value(word)[word]);

    private static string? Draws(Page page, string selector) => page.FindAll(selector)[0].Attribute(Mark);

    private static string Day(int day) =>
        "button[aria-label=\"" + new DateOnly(2026, 1, day).ToString("D", CultureInfo.GetCultureInfo("en-US")) + "\"]";

    [Fact]
    public void A_listbox_that_waits_draws_itself_on_every_event_it_hears()
    {
        var a = new Answers();

        var page = Page.Render(() => Ui.Select.Bind(() => a.One).Listbox.Searchable()[Options()]);

        Assert.Equal("toggle", Draws(page, "[popover]"));
        Assert.Equal("click mouseenter", Draws(page, "[data-ui-option]"));
        Assert.Equal("input keydown", Draws(page, "[data-ui-select-search] input"));
        Assert.Equal("click", Draws(page, "[data-ui-select-search] button"));
        Assert.Equal("keydown", Draws(page, "[data-ui-select-button]"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_pick_that_is_written_or_told_at_once_is_an_action_and_the_rest_still_only_draws(bool bound)
    {
        var a = new Answers();

        var page = Page.Render(() => bound
            ? Ui.Select.Bind(() => a.One).Live().Listbox.Searchable()[Options()]
            : Ui.Select.Value(a.One).OnChange(one => a.One = one).Listbox.Searchable()[Options()]);

        Assert.Equal("toggle", Draws(page, "[popover]"));
        Assert.Equal("mouseenter", Draws(page, "[data-ui-option]"));
        Assert.Equal("input", Draws(page, "[data-ui-select-search] input"));
        Assert.Null(Draws(page, "[data-ui-select-button]"));
    }

    [Fact]
    public void A_search_the_page_listens_to_is_an_action_at_every_key_and_so_is_its_clear_button()
    {
        var a = new Answers();

        var page = Page.Render(() => Ui.Select.Bind(() => a.One).Listbox.Searchable()[Ui.SelectSearch.OnInput(_ => { }), Options()]);

        Assert.Equal("keydown", Draws(page, "[data-ui-select-search] input"));
        Assert.Null(Draws(page, "[data-ui-select-search] button"));
    }

    [Fact]
    public void A_list_with_a_row_that_creates_keeps_its_keys_as_actions_because_enter_may_run_the_pages_code()
    {
        var a = new Answers();

        var page = Page.Render(() => Ui.Select.Bind(() => a.One).Listbox.Searchable()[Options(), Ui.SelectOptionCreate.OnClick(_ => { })["Create"]]);

        Assert.Equal("input", Draws(page, "[data-ui-select-search] input"));
        Assert.Equal("mouseenter", Draws(page, "[data-ui-option-create]"));
        Assert.Null(Draws(page, "[data-ui-select-button]"));
    }

    [Fact]
    public void A_combobox_and_a_pillbox_that_wait_draw_themselves_too()
    {
        var a = new Answers { Many = ["Alpha"] };

        var page = Page.Render(() => Fragment[
            Div.Id("combo")[Ui.Select.Bind(() => a.One).Combobox.Clearable()[Options()]],
            Div.Id("pills")[Ui.Pillbox.Bind(() => a.Many).Combobox[Words.Select(word => Ui.PillboxOption.Key(word)[word])]]
        ]);

        Assert.Equal("click blur input keydown", Draws(page, "#combo input[role=\"combobox\"]"));
        Assert.Equal("input keydown", Draws(page, "#pills input"));
        Assert.Equal("click", Draws(page, "#pills [data-value=\"Alpha\"] [data-rask-on-click]"));
        Assert.Equal("click mouseenter", Draws(page, "#pills [data-ui-listbox-option]"));
    }

    [Fact]
    public void A_calendar_pages_as_drawing_and_picks_as_drawing_only_while_it_waits()
    {
        var a = new Answers();

        var waits = Page.Render(() => Ui.Calendar.Bind(() => a.Day).SelectableHeader().Locale("en-US").On(Today));
        var live = Page.Render(() => Ui.Calendar.Bind(() => a.Day).Live().Locale("en-US").On(Today));

        Assert.Equal("click mouseenter", Draws(waits, Day(20)));
        Assert.Equal("click", Draws(waits, "button[aria-label=\"Next month\"]"));
        Assert.Equal("change", Draws(waits, "select"));
        Assert.Equal("mouseenter", Draws(live, Day(20)));
        Assert.Equal("click", Draws(live, "button[aria-label=\"Next month\"]"));
    }

    [Fact]
    public void A_panel_nobody_listens_to_sends_nothing_at_all_and_one_the_page_hears_is_an_action()
    {
        var silent = Page.Render(() => Ui.Dropdown[Ui.Button["Open"], Ui.Menu[Ui.MenuItem["One"]]]);
        var heard = Page.Render(() => Ui.Dropdown.OnToggle(_ => { })[Ui.Button["Open"], Ui.Menu[Ui.MenuItem["One"]]]);

        Assert.Null(silent.Find("[role=\"menu\"]").Attribute("data-rask-on-toggle"));
        Assert.NotNull(heard.Find("[role=\"menu\"]").Attribute("data-rask-on-toggle"));
        Assert.Null(Draws(heard, "[role=\"menu\"]"));
        Assert.Null(Draws(silent, "[role=\"menu\"]"));
    }

    [Fact]
    public void A_tab_nobody_listens_to_switches_as_drawing_and_one_the_page_hears_is_an_action()
    {
        var silent = Page.Render(() => Ui.Tabs[Ui.Tab.Name("a")["A"], Ui.Tab.Name("b")["B"]]);
        var heard = Page.Render(() => Ui.Tabs.OnChange(_ => { })[Ui.Tab.Name("a")["A"], Ui.Tab.Name("b")["B"]]);

        Assert.Equal("click", Draws(silent, "[role=\"tab\"]"));
        Assert.Null(Draws(heard, "[role=\"tab\"]"));
    }

    [Fact]
    public void A_group_of_boxes_that_waits_draws_each_tick_and_a_box_the_page_holds_does_not()
    {
        var a = new Answers();

        var page = Page.Render(() => Fragment[
            Div.Id("group")[Ui.CheckboxGroup.Bind(() => a.Many)[Words.Select(word => Ui.Checkbox.Key(word).Value(word).Label(word))]],
            Div.Id("alone")[Ui.Checkbox.Checked(false).OnChange(_ => { }).Label("Alone")]
        ]);

        Assert.Equal("change", Draws(page, "#group input"));
        Assert.Null(Draws(page, "#alone input"));
    }

    private sealed class Answers
    {
        public string? One { get; set; }

        public List<string> Many { get; set; } = [];

        public DateOnly Day { get; set; }
    }
}
