using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     The answers a pillbox, or a select of several, holds when a render brings options that no longer include
///     them — a search the page answers with other rows. Flux leaves each picked option stamped as a pill while
///     the value holds it.
/// </summary>
/// <remarks>
///     Measured on fluxui.dev/components/pillbox, "With backend search", 2026-10-13. Design, Development and
///     Marketing picked, then "Desi" searched (one row left) and "zzqq" (none): the three pills stand, in the
///     order picked, and the value is still the three. The cross on Development, whose option is not rendered,
///     takes it off — the value is the other two, and what was typed is emptied. Backspace in the empty input
///     takes the last one off. A value no option was drawn for has nothing to stamp a pill from.
/// </remarks>
public partial class UiPillboxKeptAnswersTests : global::Rask.Core.RaskMarkup
{
    private static readonly string[] All = ["Design", "Development", "Marketing", "Sales"];

    private string[] _rows = All;
    private ICollection<string> _picked = [];

    private void Search(string text) =>
        _rows = [.. All.Where(name => name.Contains(text, StringComparison.OrdinalIgnoreCase))];

    private Component Pillbox() =>
        Ui.Pillbox.Values(_picked).OnChange(values => _picked = values).Combobox.Filter(false).Placeholder("Choose tags...")[
            Ui.PillboxInput.OnInput(Search),
            _rows.Select(name => Ui.PillboxOption.Key(name).Value(name)[name])
        ];

    private Component Several() =>
        Ui.Select.Values(_picked).OnChange(values => _picked = values).Listbox.Multiple().Searchable().Filter(false)
            .Clear(Ui.SelectClear.Close).Name("tags")[
            Ui.SelectSearch.OnInput(Search),
            _rows.Select(name => Ui.SelectOption.Key(name).Value(name)[name])
        ];

    private static IEnumerable<string> Pills(Page page) =>
        page.FindAll("[data-ui-pillbox-trigger] [data-value]").Select(pill => pill.TextContent.Trim());

    private static Task Pick(Page page, string name) =>
        page.On($"[role=\"option\"][id$='-{Array.IndexOf(All, name)}']").Click();

    private async Task<Page> PickedThreeAsync()
    {
        var page = Acting.Render(Pillbox);
        await page.On("[data-ui-pillbox-trigger]").Click();
        await Pick(page, "Marketing");
        await Pick(page, "Design");
        await Pick(page, "Development");

        return page;
    }

    [Fact]
    public async Task The_pills_stand_in_the_order_picked_when_a_search_leaves_one_of_their_options()
    {
        var page = await PickedThreeAsync();

        await page.On("[data-ui-pillbox-input]").Input("Desi");

        Assert.Single(page.FindAll("[role=\"option\"]"));
        Assert.Equal(["Marketing", "Design", "Development"], Pills(page));
        Assert.Equal(["Marketing", "Design", "Development"], _picked);
    }

    [Fact]
    public async Task The_pills_stand_when_a_search_leaves_none_of_their_options()
    {
        var page = await PickedThreeAsync();

        await page.On("[data-ui-pillbox-input]").Input("zzqq");

        Assert.Empty(page.FindAll("[role=\"option\"]"));
        Assert.Equal(["Marketing", "Design", "Development"], Pills(page));
        Assert.Empty(page.FindAll("[data-ui-pillbox-placeholder]"));
    }

    [Fact]
    public async Task The_cross_on_a_pill_whose_option_is_not_among_the_rows_takes_it_out_of_the_value()
    {
        var page = await PickedThreeAsync();
        await page.On("[data-ui-pillbox-input]").Input("zzqq");

        await page.On("[data-ui-pillbox-trigger] [data-value=\"Design\"] [data-rask-on-click]").Click();

        Assert.Equal(["Marketing", "Development"], _picked);
        Assert.Equal(["Marketing", "Development"], Pills(page));
    }

    [Fact]
    public async Task Backspace_in_the_empty_input_takes_the_last_pill_off_though_its_option_is_not_among_the_rows()
    {
        var page = await PickedThreeAsync();
        await page.On("[data-ui-pillbox-input]").Input("zzqq");
        await page.On("[data-ui-pillbox-input]").Input("");
        Search("zzqq");

        await page.On("[data-ui-pillbox-input]").Raise("keydown", "{\"key\":\"Backspace\"}");

        Assert.Equal(["Marketing", "Design"], _picked);
        Assert.Equal(["Marketing", "Design"], Pills(page));
    }

    [Fact]
    public void A_value_no_render_ever_drew_an_option_for_has_no_pill()
    {
        _picked = ["Design", "Research"];

        var page = Page.Render(Pillbox);

        Assert.Equal(["Design"], Pills(page));
    }

    [Fact]
    public async Task A_value_that_left_takes_its_face_with_it_and_comes_back_without_one()
    {
        var page = await PickedThreeAsync();
        await page.On("[data-ui-pillbox-input]").Input("zzqq");

        _picked = ["Marketing"];
        await page.On("[data-ui-pillbox-trigger]").Click();
        var afterLeaving = Pills(page).ToList();
        _picked = ["Marketing", "Design"];
        await page.On("[data-ui-pillbox-trigger]").Click();

        Assert.Equal(["Marketing"], afterLeaving);
        Assert.Equal(["Marketing"], Pills(page));
    }

    [Fact]
    public async Task A_named_select_of_several_goes_on_posting_the_answers_a_search_took_out_of_the_list()
    {
        var page = Acting.Render(Several);
        await page.On("[popover]").Raise("toggle", "{\"oldState\":\"closed\",\"newState\":\"open\"}");
        await Pick(page, "Marketing");
        await Pick(page, "Design");

        await page.On("[data-ui-select-search] input").Input("Desi");

        Assert.Equal(["Design", "Marketing"], page.FindAll("input[type=\"hidden\"][name=\"tags\"]").Select(input => input.Attribute("value")));
        Assert.Equal("2 selected", page.TextOf("[data-ui-select-button]"));
    }
}
