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

    // The list itself is the browser's (`data-rask-list`, rask-list.ts): opening it, its cursor, its keys and
    // its search have no handler to name. What is left to say of is the press that commits.
    [Fact]
    public void A_listbox_that_waits_draws_a_pick_and_hears_nothing_else_of_its_list()
    {
        var a = new Answers();

        var page = Page.Render(() => Ui.Select.Bind(() => a.One).Listbox.Searchable()[Options()]);

        Assert.Equal("click", Draws(page, "[data-ui-option]"));
        Assert.Null(page.Find("[popover]").Attribute("data-rask-on-toggle"));
        Assert.Null(page.FindAll("[data-ui-option]")[0].Attribute("data-rask-on-mouseenter"));
        Assert.Null(page.Find("[data-ui-select-search] input").Attribute("data-rask-on-input"));
        Assert.Null(page.Find("[data-ui-select-search] button").Attribute("data-rask-on-click"));
        Assert.Null(page.Find("[data-ui-select-button]").Attribute("data-rask-on-keydown"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_pick_that_is_written_or_told_at_once_is_an_action(bool bound)
    {
        var a = new Answers();

        var page = Page.Render(() => bound
            ? Ui.Select.Bind(() => a.One).Live().Listbox.Searchable()[Options()]
            : Ui.Select.Value(a.One).OnChange(one => a.One = one).Listbox.Searchable()[Options()]);

        Assert.NotNull(page.FindAll("[data-ui-option]")[0].Attribute("data-rask-on-click"));
        Assert.Null(Draws(page, "[data-ui-option]"));
        Assert.Empty(page.FindAll("[" + Mark + "]"));
    }

    [Fact]
    public void A_search_the_page_listens_to_is_an_action_at_every_key_and_its_clear_button_is_the_browsers()
    {
        var a = new Answers();

        var page = Page.Render(() => Ui.Select.Bind(() => a.One).Listbox.Searchable()[Ui.SelectSearch.OnInput(_ => { }), Options()]);

        Assert.NotNull(page.Find("[data-ui-select-search] input").Attribute("data-rask-on-input"));
        Assert.Null(Draws(page, "[data-ui-select-search] input"));
        Assert.NotNull(page.Find("[data-ui-select-search] button").Attribute("data-rask-clear"));
        Assert.Null(page.Find("[data-ui-select-search] button").Attribute("data-rask-on-click"));
    }

    [Fact]
    public void A_row_that_creates_is_an_action_and_the_text_it_is_about_goes_ahead_of_its_press()
    {
        var a = new Answers();

        var page = Page.Render(() => Ui.Select.Bind(() => a.One).Listbox.Searchable()[Options(), Ui.SelectOptionCreate.OnClick(_ => { })["Create"]]);

        Assert.NotNull(page.Find("[data-ui-option-create]").Attribute("data-rask-on-click"));
        Assert.Null(Draws(page, "[data-ui-option-create]"));
        Assert.Equal("action", page.Find("[data-ui-select-search] input").Attribute("data-rask-bind-on"));
        Assert.Null(Draws(page, "[data-ui-select-search] input"));
    }

    [Fact]
    public void A_combobox_and_a_pillbox_that_wait_draw_their_picks_their_crosses_and_their_clear_button()
    {
        var a = new Answers { Picked = "Alpha", Many = ["Alpha"] };

        var page = Page.Render(() => Fragment[
            Div.Id("combo")[Ui.Select.Bind(() => a.Picked).Combobox.Clearable()[Options()]],
            Div.Id("pills")[Ui.Pillbox.Bind(() => a.Many).Combobox[Words.Select(word => Ui.PillboxOption.Key(word)[word])]]
        ]);

        Assert.Equal("click", Draws(page, "#combo [data-ui-option]"));
        Assert.Equal("click", Draws(page, "#combo button[aria-label=\"Clear selected\"]"));
        Assert.Null(Draws(page, "#combo input[role=\"combobox\"]"));
        Assert.Equal("click", Draws(page, "#pills [data-value=\"Alpha\"] [data-rask-on-click]"));
        Assert.Equal("click", Draws(page, "#pills [data-ui-listbox-option]"));
        Assert.Null(Draws(page, "#pills input"));
    }

    [Fact]
    public void A_calendar_draws_in_the_browser_and_sends_nothing_that_only_draws()
    {
        var a = new Answers();

        var page = Page.Render(() => Ui.Calendar.Bind(() => a.Day).SelectableHeader().WithToday().Locale("en-US").On(Today));

        Assert.DoesNotContain("data-rask-draws", page.Html, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(page.Html, "data-rask-on-"));
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

        public string? Picked { get; set; }

        public List<string> Many { get; set; } = [];

        public DateOnly Day { get; set; }
    }
}
