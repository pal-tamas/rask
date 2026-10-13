using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     The answer a select holds when a render brings options that no longer include it — a search the page
///     answers with other rows. Flux leaves the picked option stamped in its button while the value stands.
/// </summary>
/// <remarks>
///     Measured on fluxui.dev/components/select, 2026-10-13. A listbox whose picked option is taken out of the
///     DOM, or whose every option is replaced, goes on saying "Web development"; the backend-search combobox
///     shows "Infrastructure" in its input while its list holds only "Branding"; "3 selected" stands when the
///     three rows have gone. A value written from script that no option matches leaves a page as loaded on its
///     placeholder.
/// </remarks>
public partial class UiSelectKeptAnswerTests : global::Rask.Core.RaskMarkup
{
    private const string Opened = "{\"oldState\":\"closed\",\"newState\":\"open\"}";

    private static readonly string[] All = ["Glock", "Beretta", "Walther", "Bersa"];

    private string[] _rows = All;
    private string? _picked;
    private ICollection<string> _several = [];

    private void Search(string text) =>
        _rows = [.. All.Where(name => name.Contains(text, StringComparison.OrdinalIgnoreCase))];

    private Component Listbox() =>
        Ui.Select.Value(_picked).OnChange(value => _picked = value).Listbox.Searchable().Filter(false).Placeholder("Choose…").Name("pistol")[
            Ui.SelectSearch.OnInput(Search),
            _rows.Select(name => Ui.SelectOption.Key(name).Value(name).Icon(Ui.IconName.Check)[name])
        ];

    private Component Combobox() =>
        Ui.Select.Value(_picked).OnChange(value => _picked = value).Combobox.Filter(false).Placeholder("Choose…")[
            Ui.SelectInput.OnInput(Search),
            _rows.Select(name => Ui.SelectOption.Key(name).Value(name)[name])
        ];

    private Component Several() =>
        Ui.Select.Values(_several).OnChange(values => _several = values).Listbox.Multiple().Searchable().Filter(false)
            .Clear(Ui.SelectClear.Close).Placeholder("Choose…")[
            Ui.SelectSearch.OnInput(Search),
            _rows.Select(name => Ui.SelectOption.Key(name).Value(name)[name])
        ];

    private static async Task<Page> OpenAsync(Func<Component> select)
    {
        var page = Acting.Render(select);
        await page.On("[popover]").Raise("toggle", Opened);

        return page;
    }

    private static Task Pick(Page page, string name) =>
        page.On($"[data-ui-option][id$='-{Array.IndexOf(All, name)}']").Click();

    [Fact]
    public async Task The_button_goes_on_saying_the_picked_option_when_a_search_brings_other_rows()
    {
        var page = await OpenAsync(Listbox);
        await Pick(page, "Walther");
        var drawn = Drawn(page);
        await page.On("[popover]").Raise("toggle", Opened);

        await page.On("[data-ui-select-search] input").Input("ber");

        Assert.Equal(["Beretta", "Bersa"], page.FindAll("[data-ui-option]").Select(row => row.TextContent.Trim()));
        Assert.Equal("Walther", page.TextOf("[data-ui-select-button]"));
        Assert.Equal("Walther", page.Find("[data-ui-select-button] [data-value]").Attribute("data-value"));
        Assert.Equal("Walther", page.Find("input[type=\"hidden\"][name=\"pistol\"]").Attribute("value"));
        Assert.Equal(drawn, Drawn(page));
    }

    [Fact]
    public async Task No_row_is_marked_picked_while_the_answer_is_not_among_them()
    {
        var page = await OpenAsync(Listbox);
        await Pick(page, "Walther");
        await page.On("[popover]").Raise("toggle", Opened);

        await page.On("[data-ui-select-search] input").Input("ber");

        Assert.Empty(page.FindAll("[data-ui-option][data-selected]"));
        Assert.Equal("Beretta", page.TextOf("[data-ui-option][data-active]"));
        Assert.NotNull(page.Find("[data-ui-select-button]").Attribute("data-selected"));
    }

    [Fact]
    public async Task The_comboboxs_input_goes_on_saying_its_answer_when_the_page_answers_with_other_rows()
    {
        var page = Acting.Render(Combobox);
        await page.On("input[role=\"combobox\"]").Click();
        await Pick(page, "Walther");

        Search("ber");
        await page.On("input[role=\"combobox\"]").Raise("blur");

        Assert.Equal("Walther", _picked);
        Assert.Equal("Walther", page.Find("input[role=\"combobox\"]").Attribute("value"));
    }

    [Fact]
    public void A_value_no_render_ever_drew_an_option_for_leaves_the_placeholder()
    {
        _picked = "Luger";

        var page = Page.Render(Listbox);

        Assert.Equal("Choose…", page.TextOf("[data-ui-select-placeholder]"));
    }

    [Fact]
    public async Task A_value_changed_from_code_to_one_that_is_not_among_the_rows_says_nothing_of_the_one_before()
    {
        var page = await OpenAsync(Listbox);
        await Pick(page, "Walther");

        _picked = "Luger";
        await page.On("[popover]").Raise("toggle", Opened);

        Assert.Equal("Choose…", page.TextOf("[data-ui-select-placeholder]"));
    }

    [Fact]
    public async Task Several_answers_are_counted_from_the_value_whatever_rows_the_search_left()
    {
        var page = await OpenAsync(Several);
        await Pick(page, "Glock");
        await Pick(page, "Walther");

        await page.On("[data-ui-select-search] input").Input("ber");

        Assert.Equal(["Glock", "Walther"], _several);
        Assert.Equal("2 selected", page.TextOf("[data-ui-select-button]"));
    }

    // The shape of what the button says: its boxes and its drawings, the icon among them.
    private static (int Boxes, int Drawings) Drawn(Page page) =>
        (page.FindAll("[data-ui-select-button] div").Count, page.FindAll("[data-ui-select-button] svg").Count);
}
