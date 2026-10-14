using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's searchable listbox and its multiple selection: the search field over the options, what a search
///     matches, the "no results" row, and several answers held in a collection.
/// </summary>
/// <remarks>
///     The search is the browser's, as it is in Flux: the runtime (<c>data-rask-list</c>) hides the rows that
///     do not hold what is typed, and typing sends the page nothing — which rows, the "no results" row and the
///     keys are held to Flux in a browser (<c>UiListHookTests</c>, <c>scripts/flux/parity-select.mjs</c>) and
///     in the runtime's own fixture. Here: what a render writes for it to read, and what the page hears.
/// </remarks>
public partial class UiSelectSearchTests : global::Rask.Core.RaskMarkup
{
    private static readonly string[] Industries =
        ["Photography", "Design services", "Web development", "Accounting", "Legal services", "Consulting", "Other"];

    private sealed class Company
    {
        public List<string> Tags { get; set; } = [];

        public string[] Roles { get; set; } = [];

        public HashSet<int> Ids { get; set; } = [];
    }

    private string? _picked;
    private ICollection<string> _several = [];

    private Component Searchable() =>
        Ui.Select.Value(_picked).OnChange(value => _picked = value).Listbox.Searchable().Placeholder("Choose industries...")[
            Industries.Select(name => Ui.SelectOption.Key(name)[name])
        ];

    private Component Several(Ui.SelectClear? clear = null) =>
        Ui.Select.Values(_several).OnChange(values => _several = values).Listbox.Multiple().Searchable().Clear(clear)
            .Placeholder("Choose industries...")[
            Industries.Select(name => Ui.SelectOption.Key(name)[name])
        ];

    private static IEnumerable<string> Shown(Page page) =>
        page.FindAll("[data-ui-option]").Where(row => row.Attribute("data-hidden") is null).Select(row => row.TextContent.Trim());

    [Fact]
    public void The_search_field_sits_over_a_list_of_its_own_inside_the_popover()
    {
        var page = Page.Render(Searchable);

        var field = page.Find("[data-ui-options] > [data-ui-select-search] input");

        Assert.Equal("Search...", field.Attribute("placeholder"));
        Assert.Equal("combobox", field.Attribute("role"));
        Assert.NotNull(field.Attribute("autofocus"));
        Assert.Null(page.Find("[data-ui-options]").Attribute("role"));
        Assert.Equal(page.Find("[data-ui-options] > [role=\"listbox\"]").Id, page.Find("[data-ui-select-button]").Attribute("aria-controls"));
    }

    [Fact]
    public void The_search_field_carries_Flux_ARIA_and_keeps_the_lists_keys_through_the_runtime()
    {
        var page = Page.Render(Searchable);

        var field = page.Find("[data-ui-select-search] input");

        Assert.Equal("combobox", field.Attribute("role"));
        Assert.Null(field.Attribute("aria-expanded"));
        Assert.Null(field.Attribute("aria-controls"));
        Assert.Equal("Enter ArrowUp ArrowDown Home End PageUp PageDown", field.Attribute("data-rask-contain-keys"));
    }

    [Fact]
    public void The_empty_rows_words_are_the_selects_or_the_slots()
    {
        var worded = Page.Render(() => Ui.Select.Of<string>().Listbox.Searchable().Empty("Nothing here")[Ui.SelectOption["One"]]);
        var slotted = Page.Render(() => Ui.Select.Of<string>().Listbox.Searchable()[
            Ui.SelectOptionEmpty["No projects found."],
            Ui.SelectOption["One"]
        ]);

        var words = worded.TextOf("[data-ui-listbox-empty]");
        var slot = slotted.TextOf("[data-ui-listbox-empty]");

        Assert.Equal("Nothing here", words);
        Assert.Equal("No projects found.", slot);
    }

    [Fact]
    public void The_button_names_one_answer_and_counts_more()
    {
        _several = ["Design services"];
        var one = Page.Render(() => Several()).TextOf("[data-ui-select-button]");
        _several = ["Design services", "Other"];

        var two = Page.Render(() => Several()).TextOf("[data-ui-select-button]");
        var worded = Page.Render(() => Ui.Select.Values(_several).Listbox.Multiple().SelectedSuffix("industries selected")[
            Industries.Select(name => Ui.SelectOption.Key(name)[name])
        ]).TextOf("[data-ui-select-button]");

        Assert.Equal("Design services", one);
        Assert.Equal("2 selected", two);
        Assert.Equal("2 industries selected", worded);
    }

    [Fact]
    public async Task A_bound_list_an_array_and_a_set_each_get_their_own_kind_back()
    {
        var model = new Company();
        var tags = Acting.Render(() => Ui.Select.Bind(() => model.Tags).Listbox.Multiple()[Ui.SelectOption["core"], Ui.SelectOption["ui"]]);
        var roles = Acting.Render(() => Ui.Select.Bind(() => model.Roles).Listbox.Multiple()[Ui.SelectOption["admin"], Ui.SelectOption["editor"]]);
        var ids = Acting.Render(() => Ui.Select.Bind(() => model.Ids).Listbox.Multiple()[Ui.SelectOption.Value(1)["One"], Ui.SelectOption.Value(3)["Three"]]);

        await tags.On("[data-ui-option]:has-text(\"ui\")").Click();
        await roles.On("[data-ui-option]:has-text(\"editor\")").Click();
        await ids.On("[data-ui-option]:has-text(\"Three\")").Click();
        await Acting.Act(tags);
        await Acting.Act(roles);
        await Acting.Act(ids);

        Assert.Equal(["ui"], model.Tags);
        Assert.Equal(["editor"], model.Roles);
        Assert.Equal([3], model.Ids);
    }

    [Fact]
    public void A_named_multiple_select_posts_one_hidden_input_per_answer()
    {
        _several = ["Accounting", "Other"];

        var page = Page.Render(() => Ui.Select.Values(_several).Listbox.Multiple().Name("industries")[
            Industries.Select(name => Ui.SelectOption.Key(name)[name])
        ]);

        Assert.Equal(["Accounting", "Other"], page.FindAll("input[name=\"industries\"]").Select(input => input.Attribute("value")));
    }

    [Fact]
    public void Several_answers_are_the_listboxs_and_the_native_select_says_so()
    {
        var select = Ui.Select.Values<string>([])[Ui.SelectOption["One"]];

        var thrown = Assert.Throws<InvalidOperationException>(() => select.ToHtml());

        Assert.Contains(".Listbox", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_browser_hides_the_rows_a_search_leaves_out_and_typing_has_no_handler()
    {
        var page = Page.Render(Searchable);

        var field = page.Find("[data-ui-select-search] input");

        Assert.Equal("filter", page.Find("[data-ui-select]").Attribute("data-rask-list"));
        Assert.Null(field.Attribute("data-rask-on-input"));
        Assert.Null(field.Attribute("data-rask-on-keydown"));
        Assert.Equal(7, Shown(page).Count());
    }

    [Fact]
    public void Keywords_are_said_to_the_browser_and_drawn_nowhere()
    {
        var page = Page.Render(() => Ui.Select.Of<string>().Listbox.Searchable()[
            Ui.SelectOption.Value("fruit").Keywords("apple orange pear")["Fruit"],
            Ui.SelectOption.Value("drinks").Keywords("coffee tea juice")["Drinks"]
        ]);

        var fruit = page.Find("[data-ui-option]:has-text(\"Fruit\")");

        Assert.Equal("apple orange pear", fruit.Attribute("data-rask-list-keywords"));
        Assert.Equal("Fruit", fruit.TextContent.Trim());
    }

    [Fact]
    public void The_empty_row_is_there_all_along_and_hidden_while_the_list_has_a_row()
    {
        var some = Page.Render(Searchable).Find("[data-ui-listbox-empty]");

        var none = Page.Render(() => Ui.Select.Of<string>().Listbox.Searchable()).Find("[data-ui-listbox-empty]");

        Assert.NotNull(some.Attribute("data-hidden"));
        Assert.NotNull(some.Attribute("data-rask-list-empty"));
        Assert.Null(none.Attribute("data-hidden"));
        Assert.Equal("No results found", none.TextContent.Trim());
    }

    [Fact]
    public void The_search_fields_clear_button_empties_it_in_the_browser()
    {
        var page = Page.Render(Searchable);

        var clear = page.Find("[data-ui-select-search] button");

        Assert.Equal(page.Find("[data-ui-select-search] input").Id, clear.Attribute("data-rask-clear"));
        Assert.Null(clear.Attribute("data-rask-on-click"));
    }

    [Fact]
    public async Task With_filter_off_every_option_stays_and_the_page_hears_the_search()
    {
        var heard = "";
        var page = Acting.Render(() => Ui.Select.Of<string>().Listbox.Searchable().Filter(false)[
            Ui.SelectSearch.Placeholder("Search industries...").OnInput(text => heard = text),
            Industries.Select(name => Ui.SelectOption.Key(name)[name])
        ]);

        await page.On("[data-ui-select-search] input").Input("zz");

        Assert.Equal("zz", heard);
        Assert.Equal(7, Shown(page).Count());
        Assert.Equal(string.Empty, page.Find("[data-ui-select]").Attribute("data-rask-list"));
        Assert.Equal("Search industries...", page.Find("[data-ui-select-search] input").Attribute("placeholder"));
    }

    [Fact]
    public async Task Picking_several_switches_each_pressed_row()
    {
        var page = Acting.Render(() => Several());

        await page.On("[data-ui-option]:has-text(\"Design services\")").Click();
        await page.On("[data-ui-option]:has-text(\"Other\")").Click();
        await page.On("[data-ui-option]:has-text(\"Design services\")").Click();

        Assert.Equal(["Other"], _several);
        Assert.Equal("true", page.Find("[role=\"listbox\"]").Attribute("aria-multiselectable"));
    }

    [Fact]
    public void A_list_of_several_stays_open_at_a_pick_and_keeps_its_search_only_when_cleared_on_close()
    {
        var atPick = Page.Render(() => Several()).Find("[data-ui-select]");

        var atClose = Page.Render(() => Several(Ui.SelectClear.Close)).Find("[data-ui-select]");

        Assert.Equal("multiple filter", atPick.Attribute("data-rask-list"));
        Assert.Equal("multiple stay filter", atClose.Attribute("data-rask-list"));
    }
}
