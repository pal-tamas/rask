using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     The <c>Debounce</c> step on the fields that ask a store beside <c>Ui.SelectSearch</c> — the combobox's
///     <c>Ui.SelectInput</c>, <c>Ui.PillboxSearch</c>, <c>Ui.PillboxInput</c> and <c>Ui.Autocomplete</c> — and what
///     the page hears of a search when the control itself rewrites its field.
/// </summary>
/// <remarks>
///     The pause is counted in the browser (rask-input.ts, <c>BindTimingClientTests</c>; over a real socket,
///     <c>SearchDebounceTests</c>). Held here: the markup that asks for it, only when the step is taken, and
///     which handler hears what. Measured on fluxui.dev's backend-search examples, 2026-10-13: the search model
///     is written "" when the list shuts, the picked option's words when a combobox picks, and "" when a pill
///     is taken off.
/// </remarks>
public partial class UiSearchDebounceTests : global::Rask.Core.RaskMarkup
{
    private const string Opened = "{\"oldState\":\"closed\",\"newState\":\"open\"}";
    private const string Shut = "{\"oldState\":\"open\",\"newState\":\"closed\"}";
    private const string Input = "input[role=\"combobox\"]";

    private static readonly string[] Pistols = ["Glock", "Beretta", "Walther", "Bersa"];

    private readonly List<string> _heard = [];
    private string? _picked;
    private ICollection<string> _several = [];
    private string _text = string.Empty;

    private Component Combobox(TimeSpan? pause, bool? filter = false) =>
        Ui.Select.Value(_picked).OnChange(value => _picked = value).Combobox.Filter(filter)[
            Ui.SelectInput.OnInput(_heard.Add).Debounce(pause),
            Pistols.Select(name => Ui.SelectOption.Key(name).Value(name)[name])
        ];

    private Component Listbox() =>
        Ui.Select.Value(_picked).OnChange(value => _picked = value).Listbox.Searchable().Filter(false)[
            Ui.SelectSearch.OnInput(_heard.Add),
            Pistols.Select(name => Ui.SelectOption.Key(name).Value(name)[name])
        ];

    private Component SearchedPills(TimeSpan? pause) =>
        Ui.Pillbox.Values(_several).OnChange(values => _several = values).Filter(false)[
            Ui.PillboxSearch.OnInput(_heard.Add).Debounce(pause),
            Pistols.Select(name => Ui.PillboxOption.Key(name).Value(name)[name])
        ];

    private Component TypedPills(TimeSpan? pause) =>
        Ui.Pillbox.Values(_several).OnChange(values => _several = values).Combobox.Filter(false)[
            Ui.PillboxInput.OnInput(_heard.Add).Debounce(pause),
            Pistols.Select(name => Ui.PillboxOption.Key(name).Value(name)[name])
        ];

    private Component Suggesting(TimeSpan? pause) =>
        Ui.Autocomplete.Value(_text).OnChange(text => { _text = text; _heard.Add(text); }).Debounce(pause)[
            Pistols.Select(name => Ui.AutocompleteItem.Key(name)[name])
        ];

    // What the browser sends once typing has paused.
    private static Task Pause(Page page, string field, string text) =>
        page.On(field).Raise("pause", "{\"value\":" + System.Text.Json.JsonSerializer.Serialize(text) + "}");

    private static Task Key(Page page, string field, string key) =>
        page.On(field).Raise("keydown", $"{{\"key\":\"{key}\"}}");

    private static bool IsOpen(Page page) =>
        string.Equals(page.Find("[popover]").Attribute("data-rask-popover-open"), "true", StringComparison.Ordinal);

    // ----- Ui.SelectInput ---------------------------------------------------------------------------------

    [Fact]
    public async Task A_combobox_input_with_no_debounce_step_asks_for_no_pause_and_hears_every_keystroke()
    {
        var page = Acting.Render(() => Combobox(pause: null));

        await page.On(Input).Input("b");
        await page.On(Input).Input("be");

        Assert.Null(page.Find(Input).Attribute("data-rask-debounce"));
        Assert.Null(page.Find(Input).Attribute("data-rask-on-pause"));
        Assert.Equal(["b", "be"], _heard);
    }

    [Fact]
    public async Task A_debounced_combobox_input_the_page_filters_hears_nothing_while_open_but_the_pause()
    {
        var page = Acting.Render(() => Combobox(250.Milliseconds));
        await page.On(Input).Click();

        var field = page.Find(Input);
        await Pause(page, Input, "ber");

        Assert.Equal("250", field.Attribute("data-rask-debounce"));
        Assert.NotNull(field.Attribute("data-rask-on-pause"));
        Assert.Null(field.Attribute("data-rask-on-input"));
        Assert.Equal("ArrowDown ArrowUp Enter Escape Tab", field.Attribute("data-rask-keys"));
        Assert.Equal(["ber"], _heard);
    }

    [Fact]
    public async Task The_first_key_in_a_closed_debounced_combobox_opens_its_list_and_tells_the_page_nothing()
    {
        var page = Acting.Render(() => Combobox(250.Milliseconds));
        var closedHearsKeys = page.Find(Input).Attribute("data-rask-on-input") is not null;

        await page.On(Input).Input("b");

        Assert.True(closedHearsKeys);
        Assert.True(IsOpen(page));
        Assert.Null(page.Find(Input).Attribute("data-rask-on-input"));
        Assert.Empty(_heard);
    }

    [Fact]
    public async Task Enter_during_a_comboboxs_pause_picks_from_the_list_as_drawn_and_the_late_pause_searches_for_nothing()
    {
        var page = Acting.Render(() => Combobox(250.Milliseconds));
        await page.On(Input).Click();

        await Key(page, Input, "Enter");
        await Pause(page, Input, "ber");

        Assert.Equal("Glock", _picked);
        Assert.False(IsOpen(page));
        Assert.Equal(["Glock"], _heard);
    }

    [Fact]
    public async Task A_debounced_combobox_the_select_filters_hides_rows_at_every_key_and_tells_the_page_at_the_pause()
    {
        var page = Acting.Render(() => Combobox(250.Milliseconds, filter: null));

        await page.On(Input).Input("ber");
        var heardWhileTyping = _heard.ToList();
        var shown = page.FindAll("[data-ui-option]").Where(row => row.Attribute("data-hidden") is null).Select(row => row.TextContent.Trim()).ToList();
        await Pause(page, Input, "ber");

        Assert.Empty(heardWhileTyping);
        Assert.Equal(["Beretta", "Bersa"], shown);
        Assert.Equal(["ber"], _heard);
    }

    // ----- What the page hears when the control rewrites its own field ------------------------------------

    [Fact]
    public async Task A_combobox_that_picks_tells_the_page_the_words_its_input_now_says()
    {
        var page = Acting.Render(() => Combobox(pause: null));
        await page.On(Input).Input("ber");

        await Key(page, Input, "Enter");

        Assert.Equal("Glock", _picked);
        Assert.Equal(["ber", "Glock"], _heard);
    }

    [Fact]
    public async Task Escape_in_an_open_combobox_tells_the_page_the_search_is_over()
    {
        var page = Acting.Render(() => Combobox(pause: null));
        await page.On(Input).Input("ber");

        await Key(page, Input, "Escape");

        Assert.Equal(["ber", ""], _heard);
    }

    [Fact]
    public async Task A_combobox_loaded_with_an_answer_and_opened_and_shut_tells_the_page_nothing()
    {
        _picked = "Walther";
        var page = Acting.Render(() => Combobox(pause: null));
        await page.On(Input).Click();

        await page.On("[popover]").Raise("toggle", Shut);
        await page.On(Input).Raise("blur");

        Assert.Empty(_heard);
    }

    [Fact]
    public async Task A_listbox_shut_after_a_search_tells_the_page_its_search_is_empty_once()
    {
        var page = Acting.Render(Listbox);
        await page.On("[popover]").Raise("toggle", Opened);
        await page.On("[data-ui-select-search] input").Input("ber");

        await page.On("[popover]").Raise("toggle", Shut);
        await page.On("[popover]").Raise("toggle", Opened);
        await page.On("[popover]").Raise("toggle", Shut);

        Assert.Equal(["ber", ""], _heard);
    }

    [Fact]
    public async Task A_listbox_that_picks_after_a_search_tells_the_page_its_search_is_empty()
    {
        var page = Acting.Render(Listbox);
        await page.On("[popover]").Raise("toggle", Opened);
        await page.On("[data-ui-select-search] input").Input("ber");

        await Key(page, "[data-ui-select-search] input", "Enter");

        Assert.Equal("Glock", _picked);
        Assert.Equal(["ber", ""], _heard);
    }

    [Fact]
    public void The_search_field_of_a_list_sends_only_the_lists_own_keys_whether_it_waits_or_not()
    {
        var select = Page.Render(Listbox);
        var pills = Page.Render(() => SearchedPills(pause: null));

        var keys = select.Find("[data-ui-select-search] input").Attribute("data-rask-keys");
        var pillKeys = pills.Find("[data-ui-pillbox-search] input").Attribute("data-rask-keys");

        Assert.Equal("ArrowDown ArrowUp Enter Tab", keys);
        Assert.Equal("ArrowDown ArrowUp Enter Tab", pillKeys);
    }

    // ----- Ui.PillboxSearch ---------------------------------------------------------------------------------

    [Fact]
    public async Task A_pillbox_search_asks_for_a_pause_only_with_the_step_and_then_hears_the_pause_alone()
    {
        var plain = Acting.Render(() => SearchedPills(pause: null));
        var page = Acting.Render(() => SearchedPills(250.Milliseconds));
        await page.On("[popover]").Raise("toggle", Opened);

        var field = page.Find("[data-ui-pillbox-search] input");
        await Pause(page, "[data-ui-pillbox-search] input", "ber");

        Assert.Null(plain.Find("[data-ui-pillbox-search] input").Attribute("data-rask-debounce"));
        Assert.Equal("250", field.Attribute("data-rask-debounce"));
        Assert.NotNull(field.Attribute("data-rask-on-pause"));
        Assert.Null(field.Attribute("data-rask-on-input"));
        Assert.Equal(["ber"], _heard);
    }

    // ----- Ui.PillboxInput ----------------------------------------------------------------------------------

    [Fact]
    public async Task A_pillbox_input_asks_for_a_pause_only_with_the_step_and_then_hears_the_pause_alone()
    {
        var plain = Acting.Render(() => TypedPills(pause: null));
        var page = Acting.Render(() => TypedPills(250.Milliseconds));
        await page.On("[data-ui-pillbox-trigger]").Click();

        var field = page.Find("[data-ui-pillbox-input]");
        await Pause(page, "[data-ui-pillbox-input]", "ber");

        Assert.Null(plain.Find("[data-ui-pillbox-input]").Attribute("data-rask-debounce"));
        Assert.Equal("250", field.Attribute("data-rask-debounce"));
        Assert.NotNull(field.Attribute("data-rask-on-pause"));
        Assert.Null(field.Attribute("data-rask-on-input"));
        Assert.Equal(["ber"], _heard);
    }

    [Fact]
    public async Task A_pick_during_a_pillbox_inputs_pause_is_of_the_list_as_drawn_and_asks_the_store_nothing()
    {
        var page = Acting.Render(() => TypedPills(250.Milliseconds));
        await page.On("[data-ui-pillbox-trigger]").Click();

        await Key(page, "[data-ui-pillbox-input]", "Enter");

        Assert.Equal(["Glock"], _several);
        Assert.Empty(_heard);
    }

    // The browser sends Backspace only from an empty input (`Backspace:empty`), and an input that waits holds
    // text the page has not heard: the page's own reading of the search says nothing about it.
    [Fact]
    public async Task Backspace_from_a_pillbox_input_that_waits_takes_the_last_pill_off_whatever_the_page_last_heard()
    {
        _several = ["Glock", "Bersa"];
        var page = Acting.Render(() => TypedPills(250.Milliseconds));
        await page.On("[data-ui-pillbox-trigger]").Click();
        await Pause(page, "[data-ui-pillbox-input]", "ber");

        await Key(page, "[data-ui-pillbox-input]", "Backspace");

        Assert.Equal(["Glock"], _several);
    }

    [Fact]
    public async Task A_pill_taken_off_empties_what_was_typed_and_tells_the_page()
    {
        _several = ["Glock"];
        var page = Acting.Render(() => TypedPills(pause: null));
        await page.On("[data-ui-pillbox-input]").Input("ber");

        await page.On("[data-ui-pillbox-trigger] [data-value] [data-rask-on-click]").Click();

        Assert.Empty(_several);
        Assert.Equal(["ber", ""], _heard);
    }

    // ----- Ui.Autocomplete ----------------------------------------------------------------------------------

    [Fact]
    public async Task An_autocomplete_with_no_debounce_step_asks_for_no_pause_and_holds_the_text_until_it_is_left()
    {
        var page = Acting.Render(() => Suggesting(pause: null));

        await page.On(Input).Input("ber");

        Assert.Null(page.Find(Input).Attribute("data-rask-debounce"));
        Assert.Null(page.Find(Input).Attribute("data-rask-on-pause"));
        Assert.Empty(_heard);
    }

    [Fact]
    public async Task A_debounced_autocomplete_filters_at_every_key_and_hands_the_page_the_text_at_the_pause()
    {
        var page = Acting.Render(() => Suggesting(250.Milliseconds));

        await page.On(Input).Input("ber");
        var heardWhileTyping = _heard.ToList();
        var shown = page.FindAll("[data-ui-autocomplete-item]").Where(row => row.Attribute("data-hidden") is null).Select(row => row.TextContent.Trim()).ToList();
        await Pause(page, Input, "ber");
        await Pause(page, Input, "ber");

        Assert.Equal("250", page.Find(Input).Attribute("data-rask-debounce"));
        Assert.Empty(heardWhileTyping);
        Assert.Equal(["Beretta", "Bersa"], shown);
        Assert.Equal(["ber"], _heard);
    }

    [Fact]
    public async Task An_item_picked_during_an_autocompletes_pause_is_the_text_and_the_late_pause_changes_nothing()
    {
        var page = Acting.Render(() => Suggesting(250.Milliseconds));
        await page.On(Input).Input("ber");

        await Key(page, Input, "Enter");
        await Pause(page, Input, "ber");

        Assert.Equal("Beretta", _text);
        Assert.Equal(["Beretta"], _heard);
    }

    [Fact]
    public async Task A_bound_autocomplete_with_a_pause_writes_its_member_at_the_pause()
    {
        var model = new Holder();
        var page = Acting.Render(() => Ui.Autocomplete.Bind(() => model.City).Debounce(250.Milliseconds)[
            Pistols.Select(name => Ui.AutocompleteItem.Key(name)[name])
        ]);
        await page.On(Input).Input("ber");
        var whileTyping = model.City;

        await Pause(page, Input, "ber");

        Assert.Equal(string.Empty, whileTyping);
        Assert.Equal("ber", model.City);
    }

    private sealed class Holder
    {
        public string City { get; set; } = string.Empty;
    }
}
