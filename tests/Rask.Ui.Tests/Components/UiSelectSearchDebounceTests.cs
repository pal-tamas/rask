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
    private const string Field = "[data-ui-select-search] input";

    private static readonly string[] Industries = ["Photography", "Accounting", "Consulting", "Other"];

    private readonly List<string> _heard = [];

    private Component Select(TimeSpan? pause, bool? filter = null, bool listens = true) =>
        Ui.Select.Of<string>().Listbox.Searchable().Filter(filter)[
            listens ? Ui.SelectSearch.OnInput(_heard.Add).Debounce(pause) : Ui.SelectSearch.Debounce(pause),
            Industries.Select(name => Ui.SelectOption.Key(name)[name])
        ];

    // What the browser sends once typing has paused, or ahead of the reader's next action.
    private static Task Pause(Page page, string text) =>
        page.On(Field).Raise("pause", "{\"value\":" + System.Text.Json.JsonSerializer.Serialize(text) + "}");

    private static IEnumerable<string> Shown(Page page) =>
        page.FindAll("[data-ui-option]").Where(row => row.Attribute("data-hidden") is null).Select(row => row.TextContent.Trim());

    [Fact]
    public async Task A_search_with_no_debounce_step_asks_for_no_pause_and_hears_every_keystroke()
    {
        var page = Acting.Render(() => Select(pause: null, filter: false));

        await page.On(Field).Input("a");
        await page.On(Field).Input("ac");

        var field = page.Find(Field);
        Assert.Null(field.Attribute("data-rask-debounce"));
        Assert.Null(field.Attribute("data-rask-keys"));
        Assert.Null(field.Attribute("data-rask-on-pause"));
        Assert.Equal(["a", "ac"], _heard);
    }

    [Fact]
    public async Task A_pause_of_zero_is_every_keystroke()
    {
        var page = Acting.Render(() => Select(TimeSpan.Zero, filter: false));

        await page.On(Field).Input("a");

        Assert.Null(page.Find(Field).Attribute("data-rask-debounce"));
        Assert.Null(page.Find(Field).Attribute("data-rask-keys"));
        Assert.Equal(["a"], _heard);
    }

    [Fact]
    public async Task The_clear_button_of_a_debounced_search_tells_the_page_at_once()
    {
        var page = Acting.Render(() => Select(250.Milliseconds, filter: false));
        await Pause(page, "ac");

        await page.On("[data-ui-select-search] button").Click();

        Assert.Equal(["ac", ""], _heard);
    }

    [Fact]
    public async Task A_pause_that_ends_on_the_text_the_page_last_heard_asks_nothing_again()
    {
        var page = Acting.Render(() => Select(250.Milliseconds, filter: false));
        await Pause(page, "ac");

        await Pause(page, "ac");
        await Pause(page, "a");

        Assert.Equal(["ac", "a"], _heard);
    }

    [Fact]
    public async Task A_debounced_search_the_page_filters_hears_nothing_but_the_pause()
    {
        var page = Acting.Render(() => Select(250.Milliseconds, filter: false));

        var field = page.Find(Field);
        await Pause(page, "ac");

        Assert.Equal("250", field.Attribute("data-rask-debounce"));
        Assert.NotNull(field.Attribute("data-rask-on-pause"));
        Assert.Null(field.Attribute("data-rask-on-input"));
        Assert.Null(field.Attribute("data-rask-on-keydown"));
        Assert.Equal(["ac"], _heard);
        Assert.Equal(4, Shown(page).Count());
    }

    // The rows are hidden in the browser at every key, with nothing sent: only the pause reaches the page.
    [Fact]
    public async Task A_debounced_search_the_browser_filters_tells_the_page_at_the_pause_and_at_no_key()
    {
        var page = Acting.Render(() => Select(250.Milliseconds));

        var field = page.Find(Field);
        await Pause(page, "co");

        Assert.Equal("250", field.Attribute("data-rask-debounce"));
        Assert.Null(field.Attribute("data-rask-on-input"));
        Assert.Equal("filter", page.Find("[data-ui-select]").Attribute("data-rask-list"));
        Assert.Equal(["co"], _heard);
    }

    [Fact]
    public void A_pause_with_nothing_listening_is_not_asked_for_and_typing_sends_nothing()
    {
        var page = Page.Render(() => Select(250.Milliseconds, listens: false));

        var field = page.Find(Field);

        Assert.Null(field.Attribute("data-rask-debounce"));
        Assert.Null(field.Attribute("data-rask-on-pause"));
        Assert.Null(field.Attribute("data-rask-on-input"));
    }
}
