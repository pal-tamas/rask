using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's searchable listbox and its multiple selection: the search field over the options, what a search
///     matches, the "no results" row, and several answers held in a collection.
/// </summary>
/// <remarks>
///     The search is C#, not script: the field raises <c>input</c>, the select hides the rows that do not match
///     and renders again. A hidden row stays in the list, as it does on Flux, so nothing under the pointer moves
///     a round trip late.
/// </remarks>
public partial class UiSelectSearchTests : global::Rask.Core.RaskMarkup
{
    private const string Opened = "{\"oldState\":\"closed\",\"newState\":\"open\"}";

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

    private static async Task<Page> OpenAsync(Func<Component> select)
    {
        var page = Page.Render(select);
        await page.On("[popover]").Raise("toggle", Opened);

        return page;
    }

    private static IEnumerable<string> Shown(Page page) =>
        page.FindAll("[data-ui-option]").Where(row => row.Attribute("data-hidden") is null).Select(row => row.TextContent.Trim());

    private static Task Key(Page page, string key) =>
        page.On("[data-ui-select-search] input").Raise("keydown", $"{{\"key\":\"{key}\"}}");

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
    public async Task Typing_leaves_the_options_that_hold_the_words_anywhere_whatever_their_case()
    {
        var page = await OpenAsync(Searchable);

        await page.On("[data-ui-select-search] input").Input("CO");

        Assert.Equal(["Accounting", "Consulting"], Shown(page));
        Assert.Equal("Accounting", page.TextOf("[data-active]"));
    }

    [Fact]
    public async Task A_search_ignores_accents()
    {
        var page = await OpenAsync(() => Ui.Select.Of<string>().Listbox.Searchable()[
            Ui.SelectOption.Value("at")["Österreich"],
            Ui.SelectOption.Value("ie")["Ireland"]
        ]);

        await page.On("[data-ui-select-search] input").Input("oster");

        Assert.Equal(["Österreich"], Shown(page));
    }

    [Fact]
    public async Task Keywords_find_an_option_without_being_shown()
    {
        var page = await OpenAsync(() => Ui.Select.Of<string>().Listbox.Searchable()[
            Ui.SelectOption.Value("fruit").Keywords("apple orange pear")["Fruit"],
            Ui.SelectOption.Value("drinks").Keywords("coffee tea juice")["Drinks"]
        ]);

        await page.On("[data-ui-select-search] input").Input("apple");

        Assert.Equal(["Fruit"], Shown(page));
        Assert.DoesNotContain("apple orange pear", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_search_field_carries_Flux_ARIA_and_keeps_the_lists_keys_through_the_runtime()
    {
        var page = await OpenAsync(Searchable);

        var field = page.Find("[data-ui-select-search] input");

        Assert.Equal("combobox", field.Attribute("role"));
        Assert.Null(field.Attribute("aria-expanded"));
        Assert.Null(field.Attribute("aria-controls"));
        Assert.Equal("Enter ArrowUp ArrowDown Home End PageUp PageDown", field.Attribute("data-rask-contain-keys"));
    }

    [Fact]
    public async Task A_search_that_matches_nothing_shows_the_empty_row()
    {
        var page = await OpenAsync(Searchable);
        var before = page.Find("[data-ui-listbox-empty]").Attribute("data-hidden");

        await page.On("[data-ui-select-search] input").Input("zz");

        Assert.NotNull(before);
        Assert.Null(page.Find("[data-ui-listbox-empty]").Attribute("data-hidden"));
        Assert.Equal("No results found", page.TextOf("[data-ui-listbox-empty]"));
        Assert.Empty(Shown(page));
    }

    [Fact]
    public async Task The_empty_rows_words_are_the_selects_or_the_slots()
    {
        var worded = await OpenAsync(() => Ui.Select.Of<string>().Listbox.Searchable().Empty("Nothing here")[Ui.SelectOption["One"]]);
        var slotted = await OpenAsync(() => Ui.Select.Of<string>().Listbox.Searchable()[
            Ui.SelectOptionEmpty["No projects found."],
            Ui.SelectOption["One"]
        ]);

        var words = worded.TextOf("[data-ui-listbox-empty]");
        var slot = slotted.TextOf("[data-ui-listbox-empty]");

        Assert.Equal("Nothing here", words);
        Assert.Equal("No projects found.", slot);
    }

    [Fact]
    public async Task Enter_in_the_search_picks_the_active_match_and_the_search_is_forgotten()
    {
        var page = await OpenAsync(Searchable);
        await page.On("[data-ui-select-search] input").Input("leg");

        await Key(page, "Enter");
        var whileClosing = Shown(page).Count();
        await page.On("[popover]").Raise("toggle", "{\"oldState\":\"open\",\"newState\":\"closed\"}");

        Assert.Equal("Legal services", _picked);
        // The rows a search hid stay hidden until the browser says the list is shut: nothing moves under the pointer.
        Assert.Equal(1, whileClosing);
        Assert.Equal(7, Shown(page).Count());
        Assert.Equal(string.Empty, page.Find("[data-ui-select-search] input").Attribute("value") ?? string.Empty);
    }

    [Fact]
    public async Task The_arrows_in_the_search_walk_only_what_it_left()
    {
        var page = await OpenAsync(Searchable);
        await page.On("[data-ui-select-search] input").Input("co");

        await Key(page, "ArrowDown");
        await Key(page, "ArrowDown");

        Assert.Equal("Consulting", page.TextOf("[data-active]"));
        Assert.Equal(page.Find("[data-active]").Id, page.Find("[data-ui-select-search] input").Attribute("aria-activedescendant"));
    }

    [Fact]
    public async Task With_filter_off_every_option_stays_and_the_page_hears_the_search()
    {
        var heard = "";
        var page = await OpenAsync(() => Ui.Select.Of<string>().Listbox.Searchable().Filter(false)[
            Ui.SelectSearch.Placeholder("Search industries...").OnInput(text => heard = text),
            Industries.Select(name => Ui.SelectOption.Key(name)[name])
        ]);

        await page.On("[data-ui-select-search] input").Input("zz");

        Assert.Equal("zz", heard);
        Assert.Equal(7, Shown(page).Count());
        Assert.Equal("Search industries...", page.Find("[data-ui-select-search] input").Attribute("placeholder"));
    }

    [Fact]
    public async Task Picking_several_switches_each_row_and_leaves_the_list_open()
    {
        var page = await OpenAsync(() => Several());

        await page.On("[data-ui-option]:has-text(\"Design services\")").Click();
        await page.On("[data-ui-option]:has-text(\"Other\")").Click();
        await page.On("[data-ui-option]:has-text(\"Design services\")").Click();

        Assert.Equal(["Other"], _several);
        Assert.Equal("true", page.Find("[data-ui-select-button]").Attribute("aria-expanded"));
        Assert.Equal("true", page.Find("[role=\"listbox\"]").Attribute("aria-multiselectable"));
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
    public async Task A_pick_empties_the_search_unless_it_is_cleared_on_close()
    {
        var atPick = await OpenAsync(() => Several());
        var atClose = await OpenAsync(() => Several(Ui.SelectClear.Close));

        await atPick.On("[data-ui-select-search] input").Input("co");
        await atPick.On("[data-ui-option]:has-text(\"Consulting\")").Click();
        await atClose.On("[data-ui-select-search] input").Input("co");
        await atClose.On("[data-ui-option]:has-text(\"Accounting\")").Click();

        Assert.Equal(7, Shown(atPick).Count());
        Assert.Equal(["Accounting", "Consulting"], Shown(atClose));
    }

    [Fact]
    public async Task A_bound_list_an_array_and_a_set_each_get_their_own_kind_back()
    {
        var model = new Company();
        var tags = await OpenAsync(() => Ui.Select.Bind(() => model.Tags).Listbox.Multiple()[Ui.SelectOption["core"], Ui.SelectOption["ui"]]);
        var roles = await OpenAsync(() => Ui.Select.Bind(() => model.Roles).Listbox.Multiple()[Ui.SelectOption["admin"], Ui.SelectOption["editor"]]);
        var ids = await OpenAsync(() => Ui.Select.Bind(() => model.Ids).Listbox.Multiple()[Ui.SelectOption.Value(1)["One"], Ui.SelectOption.Value(3)["Three"]]);

        await tags.On("[data-ui-option]:has-text(\"ui\")").Click();
        await roles.On("[data-ui-option]:has-text(\"editor\")").Click();
        await ids.On("[data-ui-option]:has-text(\"Three\")").Click();

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
}
