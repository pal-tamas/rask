using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's <c>flux:autocomplete</c>: an input that suggests what to type and writes the picked suggestion into
///     itself.
/// </summary>
/// <remarks>
///     The list is the browser's (<c>data-rask-list</c>): the lit row, the filter, whether it is open and the
///     pick that writes an item into the input send the page nothing, and are held to fluxui.dev key by key in a
///     browser (<c>scripts/flux/parity-autocomplete.mjs</c>, <c>UiListHookTests</c>). The page hears the input's
///     text as it hears any input's. Here: what a render writes, and that text arriving.
/// </remarks>
public partial class UiAutocompleteTests : global::Rask.Core.RaskMarkup
{
    private static readonly string[] States = ["Alabama", "Arkansas", "California", "New Mexico", "Texas"];

    private string _state = "";

    private Component Autocomplete() =>
        Ui.Autocomplete.Value(_state).OnChange(text => _state = text).Label("State of residence")[
            States.Select(state => Ui.AutocompleteItem.Key(state)[state])
        ];

    [Fact]
    public void The_field_sits_inside_the_root_beside_the_list_it_drops()
    {
        var html = Autocomplete().ToHtml();

        var page = Page.Render(Autocomplete);

        Assert.StartsWith("<div class=\"block\" data-ui-autocomplete data-rask-list=\"text filter\"><div", html, StringComparison.Ordinal);
        Assert.Equal("State of residence", page.TextOf("[data-ui-autocomplete] > [data-ui-field] > label"));
        Assert.NotNull(page.Find("[data-ui-autocomplete] > [data-ui-autocomplete-items][popover]"));
    }

    [Fact]
    public void The_input_is_a_combobox_over_the_list()
    {
        var page = Page.Render(Autocomplete);

        var input = page.Find("[data-ui-input] > input");

        Assert.Equal("combobox", input.Attribute("role"));
        Assert.Equal("list", input.Attribute("aria-autocomplete"));
        Assert.Equal("listbox", input.Attribute("aria-haspopup"));
        Assert.Equal("false", input.Attribute("aria-expanded"));
        Assert.Equal("off", input.Attribute("autocomplete"));
        Assert.Equal(page.Find("[role=\"listbox\"]").Id, input.Attribute("aria-controls"));
    }

    [Fact]
    public void The_list_says_it_takes_several_answers_as_Flux_writes_it()
    {
        var page = Page.Render(Autocomplete);

        var list = page.Find("[data-ui-autocomplete-items]");

        Assert.Equal("true", list.Attribute("aria-multiselectable"));
        Assert.Equal("-1", list.Attribute("tabindex"));
        Assert.All(page.FindAll("[data-ui-autocomplete-item]"), row => Assert.Equal("false", row.Attribute("aria-selected")));
    }

    [Fact]
    public void The_inputs_props_reach_the_input()
    {
        var html = Ui.Autocomplete.Value("").Sm.Filled.Placeholder("Search states").Icon(Ui.IconName.MagnifyingGlass).Clearable()
            .ContainerClass("max-h-80").InputClass("font-mono").ToHtml();

        var page = Page.Render(() => Ui.Autocomplete.Value("").Sm.Filled.Placeholder("Search states").ContainerClass("max-h-80").InputClass("font-mono"));

        Assert.Equal("Search states", page.Find("input").Attribute("placeholder"));
        Assert.Contains("h-8", page.Find("input").Attribute("class"), StringComparison.Ordinal);
        Assert.Contains("font-mono", page.Find("input").Attribute("class"), StringComparison.Ordinal);
        Assert.Contains("max-h-80", page.Find("[popover]").Attribute("class"), StringComparison.Ordinal);
        Assert.Contains("data-ui-clear-button", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_list_is_the_browsers_to_keep_and_its_answer_is_the_inputs_text()
    {
        var page = Page.Render(Autocomplete);

        var root = page.Find("[data-ui-autocomplete]");

        Assert.Equal("text filter", root.Attribute("data-rask-list"));
        Assert.False(page.Exists("[data-active]"));
        Assert.Null(page.Find("input").Attribute("aria-activedescendant"));
        Assert.Null(page.Find("[popover]").Attribute("data-rask-popover-open"));
        Assert.NotNull(page.Find("[data-ui-autocomplete-items] > [data-rask-list-empty][data-hidden]"));
    }

    // Every key, every letter, every row the pointer crossed and every pick were a round trip each. A pick is
    // the browser writing the row's words into the input, so a row has nothing for the page to hear either.
    [Theory]
    [InlineData("input", "data-rask-on-keydown")]
    [InlineData("input", "data-rask-on-input")]
    [InlineData("input", "data-rask-on-click")]
    [InlineData("[popover]", "data-rask-on-toggle")]
    [InlineData("[data-ui-autocomplete-item]", "data-rask-on-mouseenter")]
    [InlineData("[data-ui-autocomplete-item]", "data-rask-on-click")]
    public void Moving_about_the_list_and_picking_from_it_have_no_handler_for_the_page_to_hear(string part, string handler)
    {
        var page = Page.Render(Autocomplete);

        var hearing = page.FindAll(part).Select(element => element.Attribute(handler));

        Assert.All(hearing, Assert.Null);
    }

    [Fact]
    public async Task The_page_that_holds_the_text_hears_it_when_the_input_says_it_changed()
    {
        var page = Page.Render(Autocomplete);

        // What the browser raises on a pick, on Escape, and when an input typed into is left.
        await page.On("input[role=\"combobox\"]").Change("California");

        Assert.Equal("California", _state);
        Assert.Equal("California", page.Find("input").Attribute("value"));
    }

    [Fact]
    public void A_bound_autocomplete_waits_for_the_next_action_and_a_live_one_does_not()
    {
        var model = new Address();

        var waiting = Page.Render(() => Ui.Autocomplete.Bind(() => model.State)[Ui.AutocompleteItem["Texas"]]).Find("input");
        var live = Page.Render(() => Ui.Autocomplete.Bind(() => model.State).Live()[Ui.AutocompleteItem["Texas"]]).Find("input");

        Assert.Equal("action", waiting.Attribute("data-rask-bind-on"));
        Assert.Null(live.Attribute("data-rask-bind-on"));
    }

    [Fact]
    public void A_disabled_item_says_so()
    {
        var page = Page.Render(() => Ui.Autocomplete.Value(_state).OnChange(text => _state = text)[
            Ui.AutocompleteItem.Disabled()["Alabama"],
            Ui.AutocompleteItem["Arkansas"]
        ]);

        var rows = page.FindAll("[data-ui-autocomplete-item]");

        Assert.Equal("true", rows[0].Attribute("aria-disabled"));
        Assert.Null(rows[1].Attribute("aria-disabled"));
    }

    private sealed class Address
    {
        public string State { get; set; } = "";
    }
}
