using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     <c>Ui.SelectSearch.Debounce(…)</c>: what the search field asks of the runtime so that the page's
///     <c>OnInput</c> hears a pause instead of every keystroke — Flux's <c>wire:model.live.debounce</c>.
/// </summary>
/// <remarks>
///     The pause itself is counted in the browser (rask-input.ts, held by <c>BindTimingClientTests</c> and, over a
///     real socket, <c>SelectSearchDebounceTests</c>). What is held here is the markup that asks for it, and which
///     handler hears which event.
/// </remarks>
public partial class UiSelectSearchDebounceTests : global::Rask.Core.RaskMarkup
{
    private const string Opened = "{\"oldState\":\"closed\",\"newState\":\"open\"}";
    private const string Field = "[data-ui-select-search] input";

    private static readonly string[] Industries = ["Photography", "Accounting", "Consulting", "Other"];

    private readonly List<string> _heard = [];

    private Component Select(TimeSpan? pause, bool? filter = null, bool listens = true) =>
        Ui.Select.Of<string>().Listbox.Searchable().Filter(filter)[
            listens ? Ui.SelectSearch.OnInput(_heard.Add).Debounce(pause) : Ui.SelectSearch.Debounce(pause),
            Industries.Select(name => Ui.SelectOption.Key(name)[name])
        ];

    private static async Task<Page> OpenAsync(Func<Component> select)
    {
        var page = Acting.Render(select);
        await page.On("[popover]").Raise("toggle", Opened);

        return page;
    }

    // What the browser sends once typing has paused, or ahead of the reader's next action.
    private static Task Pause(Page page, string text) =>
        page.On(Field).Raise("pause", "{\"value\":" + System.Text.Json.JsonSerializer.Serialize(text) + "}");

    private static IEnumerable<string> Shown(Page page) =>
        page.FindAll("[data-ui-option]").Where(row => row.Attribute("data-hidden") is null).Select(row => row.TextContent.Trim());

    [Fact]
    public async Task A_search_with_no_debounce_step_asks_for_no_pause_and_hears_every_keystroke()
    {
        var page = await OpenAsync(() => Select(pause: null, filter: false));

        await page.On(Field).Input("a");
        await page.On(Field).Input("ac");

        var field = page.Find(Field);
        Assert.Null(field.Attribute("data-rask-debounce"));
        Assert.Null(field.Attribute("data-rask-on-pause"));
        Assert.Equal(["a", "ac"], _heard);
    }

    [Fact]
    public async Task A_debounced_search_the_page_filters_hears_nothing_but_the_pause_and_the_lists_own_keys()
    {
        var page = await OpenAsync(() => Select(250.Milliseconds, filter: false));

        var field = page.Find(Field);
        await Pause(page, "ac");

        Assert.Equal("250", field.Attribute("data-rask-debounce"));
        Assert.Equal("ArrowDown ArrowUp Enter Tab", field.Attribute("data-rask-keys"));
        Assert.NotNull(field.Attribute("data-rask-on-pause"));
        Assert.Null(field.Attribute("data-rask-on-input"));
        Assert.Equal(["ac"], _heard);
        Assert.Equal(4, Shown(page).Count());
    }

    [Fact]
    public async Task A_debounced_search_the_select_filters_hides_rows_at_every_key_and_tells_the_page_at_the_pause()
    {
        var page = await OpenAsync(() => Select(250.Milliseconds));

        await page.On(Field).Input("c");
        await page.On(Field).Input("co");
        var heardWhileTyping = _heard.ToList();
        var shownWhileTyping = Shown(page).ToList();
        await Pause(page, "co");

        Assert.Equal("250", page.Find(Field).Attribute("data-rask-debounce"));
        Assert.Empty(heardWhileTyping);
        Assert.Equal(["Accounting", "Consulting"], shownWhileTyping);
        Assert.Equal(["co"], _heard);
        Assert.Equal(["Accounting", "Consulting"], Shown(page));
    }

    [Fact]
    public async Task A_pause_of_zero_is_every_keystroke()
    {
        var page = await OpenAsync(() => Select(TimeSpan.Zero, filter: false));

        await page.On(Field).Input("a");

        Assert.Null(page.Find(Field).Attribute("data-rask-debounce"));
        Assert.Equal(["a"], _heard);
    }

    [Fact]
    public async Task A_pause_with_nothing_listening_is_not_asked_for()
    {
        var page = await OpenAsync(() => Select(250.Milliseconds, listens: false));

        await page.On(Field).Input("co");

        Assert.Null(page.Find(Field).Attribute("data-rask-debounce"));
        Assert.Null(page.Find(Field).Attribute("data-rask-on-pause"));
        Assert.Equal(["Accounting", "Consulting"], Shown(page));
    }

    [Fact]
    public async Task The_clear_button_of_a_debounced_search_tells_the_page_at_once()
    {
        var page = await OpenAsync(() => Select(250.Milliseconds, filter: false));
        await Pause(page, "ac");

        await page.On("[data-ui-select-search] button").Click();

        Assert.Equal(["ac", ""], _heard);
    }

    [Fact]
    public async Task A_pause_that_ends_on_the_text_the_page_last_heard_asks_nothing_again()
    {
        var page = await OpenAsync(() => Select(250.Milliseconds, filter: false));
        await Pause(page, "ac");

        await Pause(page, "ac");
        await Pause(page, "a");

        Assert.Equal(["ac", "a"], _heard);
    }

    [Fact]
    public async Task Enter_during_the_pause_picks_from_the_list_as_drawn_and_the_pause_that_ends_later_asks_nothing()
    {
        string? picked = null;
        var page = await OpenAsync(() => Ui.Select.Value(picked).OnChange(value => picked = value).Listbox.Searchable().Filter(false)[
            Ui.SelectSearch.OnInput(_heard.Add).Debounce(250.Milliseconds),
            Industries.Select(name => Ui.SelectOption.Key(name)[name])
        ]);

        await page.On(Field).Raise("keydown", "{\"key\":\"Enter\"}");
        await Pause(page, "ac");

        Assert.Equal("Photography", picked);
        Assert.Empty(_heard);
    }
}
