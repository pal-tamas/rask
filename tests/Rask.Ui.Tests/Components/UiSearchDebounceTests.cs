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
///     is taken off. The list is the browser's (<c>data-rask-list</c>, rask-list.ts), so it is the browser that
///     rewrites the field and says so through the field's own <c>input</c> — only when the page had heard a
///     search, which <c>SearchDebounceTests</c> and <c>UiListHookTests</c> hold. Here the browser's part is
///     played by the test: a row's click is the pick, and the field's handler hears what the field says.
/// </remarks>
public partial class UiSearchDebounceTests : global::Rask.Core.RaskMarkup
{
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

    private static string? ListKind(Page page, string root) => page.Find(root).Attribute("data-rask-list");

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
    public async Task A_debounced_combobox_input_the_page_filters_hears_nothing_but_the_pause()
    {
        var page = Acting.Render(() => Combobox(250.Milliseconds));

        var field = page.Find(Input);
        await Pause(page, Input, "ber");

        Assert.Equal("250", field.Attribute("data-rask-debounce"));
        Assert.NotNull(field.Attribute("data-rask-on-pause"));
        Assert.Null(field.Attribute("data-rask-on-input"));
        Assert.Null(field.Attribute("data-rask-on-keydown"));
        Assert.Equal(["ber"], _heard);
    }

    // Opening on the first key is the browser's: a debounced combobox has no handler for a key, shut or open.
    [Fact]
    public void A_debounced_combobox_hears_no_key_and_nothing_of_its_list_opening()
    {
        var page = Page.Render(() => Combobox(250.Milliseconds));

        var field = page.Find(Input);

        Assert.Null(field.Attribute("data-rask-on-input"));
        Assert.Null(field.Attribute("data-rask-on-click"));
        Assert.Null(page.Find("[popover]").Attribute("data-rask-on-toggle"));
        Assert.Equal("strict", ListKind(page, "[data-ui-select]"));
    }

    // Enter is the browser pressing the lit row, and the pause being counted is called off there. What the
    // input then says, the answer's words, arrives as its pause does.
    [Fact]
    public async Task A_row_pressed_during_a_comboboxs_pause_is_picked_from_the_list_as_drawn_and_the_page_hears_its_words_once()
    {
        var page = Acting.Render(() => Combobox(250.Milliseconds));

        await page.On("[data-ui-option][id$='-0']").Click();
        await Pause(page, Input, "Glock");
        await Pause(page, Input, "Glock");

        Assert.Equal("Glock", _picked);
        Assert.Equal(["Glock"], _heard);
    }

    [Fact]
    public async Task A_debounced_combobox_the_browser_filters_tells_the_page_at_the_pause_and_at_no_key()
    {
        var page = Acting.Render(() => Combobox(250.Milliseconds, filter: null));

        var field = page.Find(Input);
        await Pause(page, Input, "ber");

        Assert.Null(field.Attribute("data-rask-on-input"));
        Assert.Equal("filter strict", ListKind(page, "[data-ui-select]"));
        Assert.Equal(["ber"], _heard);
    }

    // ----- What the page hears when the control rewrites its own field ------------------------------------

    [Fact]
    public async Task A_combobox_that_picks_hears_the_words_its_input_goes_on_to_say()
    {
        var page = Acting.Render(() => Combobox(pause: null));
        await page.On(Input).Input("ber");

        await page.On("[data-ui-option][id$='-0']").Click();
        await page.On(Input).Input("Glock");

        Assert.Equal("Glock", _picked);
        Assert.Equal(["ber", "Glock"], _heard);
    }

    [Fact]
    public async Task A_combobox_hears_its_input_emptied_as_it_hears_it_typed()
    {
        var page = Acting.Render(() => Combobox(pause: null));
        await page.On(Input).Input("ber");

        await page.On(Input).Input(string.Empty);

        Assert.Equal(["ber", ""], _heard);
    }

    [Fact]
    public void A_combobox_loaded_with_an_answer_has_nothing_that_tells_the_page_it_was_opened_or_shut_or_left()
    {
        _picked = "Walther";

        var page = Page.Render(() => Combobox(pause: null));

        Assert.Null(page.Find(Input).Attribute("data-rask-on-click"));
        Assert.Null(page.Find(Input).Attribute("data-rask-on-blur"));
        Assert.Null(page.Find(Input).Attribute("data-rask-on-keydown"));
        Assert.Null(page.Find("[popover]").Attribute("data-rask-on-toggle"));
        Assert.Equal("Walther", page.Find(Input).Attribute("value"));
    }

    [Fact]
    public async Task A_listbox_hears_its_search_emptied_as_it_hears_it_typed_and_nothing_of_the_list_shutting()
    {
        var page = Acting.Render(Listbox);
        await page.On("[data-ui-select-search] input").Input("ber");

        await page.On("[data-ui-select-search] input").Input(string.Empty);

        Assert.Equal(["ber", ""], _heard);
        Assert.Null(page.Find("[popover]").Attribute("data-rask-on-toggle"));
    }

    [Fact]
    public async Task A_listbox_that_picks_after_a_search_hears_its_search_emptied()
    {
        var page = Acting.Render(Listbox);
        await page.On("[data-ui-select-search] input").Input("ber");

        await page.On("[data-ui-option][id$='-0']").Click();
        await page.On("[data-ui-select-search] input").Input(string.Empty);

        Assert.Equal("Glock", _picked);
        Assert.Equal(["ber", ""], _heard);
    }

    [Fact]
    public void The_search_field_of_a_list_hears_no_key_whether_it_waits_or_not()
    {
        var select = Page.Render(Listbox);
        var pills = Page.Render(() => SearchedPills(250.Milliseconds));

        var field = select.Find("[data-ui-select-search] input");
        var pillField = pills.Find("[data-ui-pillbox-search] input");

        Assert.Null(field.Attribute("data-rask-on-keydown"));
        Assert.Null(field.Attribute("data-rask-keys"));
        Assert.Null(pillField.Attribute("data-rask-on-keydown"));
        Assert.Null(pillField.Attribute("data-rask-keys"));
    }

    // ----- Ui.PillboxSearch ---------------------------------------------------------------------------------

    [Fact]
    public async Task A_pillbox_search_asks_for_a_pause_only_with_the_step_and_then_hears_the_pause_alone()
    {
        var plain = Acting.Render(() => SearchedPills(pause: null));
        var page = Acting.Render(() => SearchedPills(250.Milliseconds));

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

        await page.On("[data-ui-listbox-option][id$='-0']").Click();

        Assert.Equal(["Glock"], _several);
        Assert.Empty(_heard);
    }

    // Backspace in an empty input is the browser pressing the last cross (`data-rask-list-remove`), and an input
    // that waits holds text the page has not heard: the page's own reading of the search says nothing about it.
    [Fact]
    public async Task The_last_cross_of_a_pillbox_whose_input_waits_takes_its_pill_off_whatever_the_page_last_heard()
    {
        _several = ["Glock", "Bersa"];
        var page = Acting.Render(() => TypedPills(250.Milliseconds));
        await Pause(page, "[data-ui-pillbox-input]", "ber");

        await page.On("[data-ui-pillbox-trigger] [data-value=\"Bersa\"] [data-rask-list-remove]").Click();

        Assert.Equal(["Glock"], _several);
    }

    [Fact]
    public async Task A_pill_taken_off_is_out_of_the_value_and_the_page_hears_the_input_emptied()
    {
        _several = ["Glock"];
        var page = Acting.Render(() => TypedPills(pause: null));
        await page.On("[data-ui-pillbox-input]").Input("ber");

        await page.On("[data-ui-pillbox-trigger] [data-value] [data-rask-list-remove]").Click();
        await page.On("[data-ui-pillbox-input]").Input(string.Empty);

        Assert.Empty(_several);
        Assert.Equal(["ber", ""], _heard);
    }

    // ----- Ui.Autocomplete ----------------------------------------------------------------------------------

    [Fact]
    public async Task An_autocomplete_with_no_debounce_step_asks_for_no_pause_and_hears_the_text_when_it_is_left()
    {
        var page = Acting.Render(() => Suggesting(pause: null));

        var field = page.Find(Input);
        await page.On(Input).Change("ber");

        Assert.Null(field.Attribute("data-rask-debounce"));
        Assert.Null(field.Attribute("data-rask-on-pause"));
        Assert.Null(field.Attribute("data-rask-on-input"));
        Assert.Equal(["ber"], _heard);
    }

    [Fact]
    public async Task A_debounced_autocomplete_is_filtered_by_the_browser_and_hands_the_page_the_text_at_the_pause()
    {
        var page = Acting.Render(() => Suggesting(250.Milliseconds));

        var field = page.Find(Input);
        await Pause(page, Input, "ber");
        await Pause(page, Input, "ber");

        Assert.Equal("250", field.Attribute("data-rask-debounce"));
        Assert.Null(field.Attribute("data-rask-on-input"));
        Assert.Equal("text filter", ListKind(page, "[data-ui-autocomplete]"));
        Assert.Equal(["ber"], _heard);
    }

    [Fact]
    public async Task A_pause_of_none_on_an_autocomplete_is_every_keystroke()
    {
        var page = Acting.Render(() => Suggesting(TimeSpan.Zero));

        await page.On(Input).Input("b");
        await page.On(Input).Input("be");

        Assert.Null(page.Find(Input).Attribute("data-rask-debounce"));
        Assert.Equal(["b", "be"], _heard);
    }

    // A pick is the browser writing the item into the input and raising its `change`; the pause it restarted
    // ends on the same text.
    [Fact]
    public async Task An_item_picked_during_an_autocompletes_pause_is_the_text_and_the_late_pause_changes_nothing()
    {
        var page = Acting.Render(() => Suggesting(250.Milliseconds));

        await page.On(Input).Change("Beretta");
        await Pause(page, Input, "Beretta");

        Assert.Equal("Beretta", _text);
        Assert.Equal(["Beretta"], _heard);
    }

    [Fact]
    public async Task A_bound_autocomplete_with_a_pause_counts_it_as_the_input_does_and_writes_its_member_then()
    {
        var model = new Holder();
        var page = Acting.Render(() => Ui.Autocomplete.Bind(() => model.City).Debounce(250.Milliseconds)[
            Pistols.Select(name => Ui.AutocompleteItem.Key(name)[name])
        ]);
        var whileTyping = model.City;

        var field = page.Find(Input);
        await page.On(Input).Input("ber");

        Assert.Equal("250", field.Attribute("data-rask-debounce"));
        Assert.Null(field.Attribute("data-rask-on-pause"));
        Assert.Equal(string.Empty, whileTyping);
        Assert.Equal("ber", model.City);
    }

    private sealed class Holder
    {
        public string City { get; set; } = string.Empty;
    }
}
