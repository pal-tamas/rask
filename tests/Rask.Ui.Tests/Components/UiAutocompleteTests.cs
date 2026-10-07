using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's <c>flux:autocomplete</c>: an input that suggests what to type and writes the picked suggestion into
///     itself.
/// </summary>
/// <remarks>As recorded on fluxui.dev, key by key; <c>scripts/flux/parity-autocomplete.mjs --record</c> replays the walk.</remarks>
public partial class UiAutocompleteTests : global::Rask.Core.RaskMarkup
{
    private static readonly string[] States = ["Alabama", "Arkansas", "California", "New Mexico", "Texas"];

    private string _state = "";

    private Component Autocomplete() =>
        Ui.Autocomplete.Value(_state).OnChange(text => _state = text).Label("State of residence")[
            States.Select(state => Ui.AutocompleteItem.Key(state)[state])
        ];

    private static Task Key(Page page, string key) =>
        page.On("input[role=\"combobox\"]").Raise("keydown", $"{{\"key\":\"{key}\"}}");

    private static bool IsOpen(Page page) =>
        string.Equals(page.Find("[popover]").Attribute("data-rask-popover-open"), "true", StringComparison.Ordinal);

    private static IEnumerable<string> Shown(Page page) =>
        page.FindAll("[data-ui-autocomplete-item]").Where(row => row.Attribute("data-hidden") is null).Select(row => row.TextContent.Trim());

    [Fact]
    public void The_field_sits_inside_the_root_beside_the_list_it_drops()
    {
        var html = Autocomplete().ToHtml();

        var page = Page.Render(Autocomplete);

        Assert.StartsWith("<div class=\"block\" data-ui-autocomplete><div", html, StringComparison.Ordinal);
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
    public async Task An_arrow_opens_the_list_on_its_first_item()
    {
        var page = Page.Render(Autocomplete);

        await Key(page, Keys.ArrowDown);

        Assert.True(IsOpen(page));
        Assert.Equal("Alabama", page.TextOf("[data-active]"));
        Assert.Equal(page.Find("[data-active]").Id, page.Find("input").Attribute("aria-activedescendant"));
    }

    [Fact]
    public async Task The_arrows_stop_at_either_end()
    {
        var page = Page.Render(Autocomplete);
        await Key(page, Keys.ArrowDown);

        await Key(page, Keys.ArrowUp);
        var top = page.TextOf("[data-active]");
        for (var i = 0; i < States.Length + 2; i++)
        {
            await Key(page, Keys.ArrowDown);
        }

        Assert.Equal("Alabama", top);
        Assert.Equal("Texas", page.TextOf("[data-active]"));
    }

    [Fact]
    public async Task Enter_writes_the_active_item_into_the_input_and_closes()
    {
        var page = Page.Render(Autocomplete);
        await Key(page, Keys.ArrowDown);
        await Key(page, Keys.ArrowDown);

        await Key(page, Keys.Enter);

        Assert.Equal("Arkansas", _state);
        Assert.False(IsOpen(page));
        Assert.Equal("true", page.Find("[data-ui-autocomplete-item]:has-text(\"Arkansas\")").Attribute("aria-selected"));
    }

    [Fact]
    public async Task Typing_on_after_a_pick_opens_the_list_again_on_the_whole_text()
    {
        var page = Page.Render(Autocomplete);
        await Key(page, Keys.ArrowDown);
        await Key(page, Keys.Enter);

        await page.On("input[role=\"combobox\"]").Input("Alabamax");

        Assert.Equal("Alabama", _state);
        Assert.True(IsOpen(page));
        Assert.Empty(Shown(page));
    }

    [Fact]
    public async Task Typing_opens_the_list_on_the_items_that_hold_the_text()
    {
        var page = Page.Render(Autocomplete);

        await page.On("input[role=\"combobox\"]").Input("X");

        Assert.True(IsOpen(page));
        Assert.Equal(["New Mexico", "Texas"], Shown(page));
        Assert.Equal("New Mexico", page.TextOf("[data-active]"));
    }

    [Fact]
    public async Task Enter_does_nothing_when_no_item_matches()
    {
        var page = Page.Render(Autocomplete);
        await page.On("input[role=\"combobox\"]").Input("zz");

        await Key(page, Keys.Enter);

        Assert.Empty(Shown(page));
        Assert.True(IsOpen(page));
        Assert.Equal("", _state);
    }

    [Fact]
    public async Task Escape_closes_the_list_and_empties_the_input()
    {
        _state = "Texas";
        var page = Page.Render(Autocomplete);
        await Key(page, Keys.ArrowDown);

        await Key(page, Keys.Escape);

        Assert.False(IsOpen(page));
        Assert.Equal("", _state);
    }

    [Fact]
    public async Task Tab_closes_the_list_and_keeps_what_was_typed()
    {
        var page = Page.Render(Autocomplete);
        await page.On("input[role=\"combobox\"]").Input("Tex");

        await Key(page, Keys.Tab);

        Assert.False(IsOpen(page));
        Assert.Equal(States, Shown(page));
    }

    [Fact]
    public async Task A_click_on_an_item_picks_it()
    {
        var page = Page.Render(Autocomplete);
        await page.On("input[role=\"combobox\"]").Click();

        await page.On("[data-ui-autocomplete-item]:has-text(\"California\")").Click();

        Assert.Equal("California", _state);
        Assert.False(IsOpen(page));
    }

    [Fact]
    public async Task A_disabled_item_is_shown_and_cannot_be_picked()
    {
        var page = Page.Render(() => Ui.Autocomplete.Value(_state).OnChange(text => _state = text)[
            Ui.AutocompleteItem.Disabled()["Alabama"],
            Ui.AutocompleteItem["Arkansas"]
        ]);

        await Key(page, Keys.ArrowDown);

        Assert.Equal("true", page.Find("[data-ui-autocomplete-item]:has-text(\"Alabama\")").Attribute("aria-disabled"));
        Assert.Equal("Arkansas", page.TextOf("[data-active]"));
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
}
