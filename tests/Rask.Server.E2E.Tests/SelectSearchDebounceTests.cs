using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     <c>Ui.SelectSearch.Debounce(…)</c> in a real browser over the real socket: a search that asks a store
///     hears one text once typing has paused, and a pick made during the pause is the row that was pressed.
/// </summary>
/// <remarks>
///     The pause is counted by the page's own timers, so the page's clock is the test's (<see cref="PageClock" />):
///     it stands still while the browser types and moves only by what the test runs it for. "Nothing was sent" is
///     counted on the socket itself.
/// </remarks>
public sealed class SelectSearchDebounceTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const int Pause = SelectSearchDebouncePage.PauseMilliseconds;

    private static ILocator Field(IPage page, string select) => page.Locator($"#{select} [data-ui-select-search] input");

    private static ILocator Option(IPage page, string select, string name) =>
        page.Locator($"#{select} [data-ui-option]:not([data-hidden])").Filter(new() { HasText = name });

    private async Task<HookSession> OpenAsync()
    {
        var session = await HookSession.OpenAsync<SelectSearchDebouncePage>(playwright, beforeLoad: async page =>
        {
            await SentFrames.Count(page);
            await PageClock.Install(page);
        });
        // A press the page answers: the socket is up before the page's time is stopped (SearchDebounceTests says why).
        await session.Page.ClickAsync("#ready");
        await Expect(session.Page.Locator("#presses")).ToHaveTextAsync("1");

        return session;
    }

    // The list open and its search field focused, with the page's time stopped from then on.
    private static async Task OpenListAsync(IPage page, string select)
    {
        await OpenWhileTimeRunsAsync(page, select);
        await PageClock.StopAsync(page);
    }

    private static async Task OpenWhileTimeRunsAsync(IPage page, string select)
    {
        await page.ClickAsync($"#{select} [data-ui-select-button]");
        await Expect(page.Locator($"#{select} [data-ui-select-button]")).ToHaveAttributeAsync("aria-expanded", "true");
        await Expect(Field(page, select)).ToBeFocusedAsync();
    }

    [Fact]
    public async Task Typing_a_search_the_page_answers_sends_nothing_until_the_pause_and_then_the_final_text_once()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await OpenListAsync(page, "store");
        var before = await SentFrames.SoFarAsync(page);

        await Field(page, "store").PressSequentiallyAsync("ber");
        await page.Clock.RunForAsync(Pause - 1);
        var whileTyping = await SentFrames.SoFarAsync(page) - before;
        await page.Clock.RunForAsync(1);
        await Expect(page.Locator("#searches")).ToHaveTextAsync("[ber]");
        await page.Clock.RunForAsync(Pause * 3);
        var afterPause = await SentFrames.SoFarAsync(page) - before;

        Assert.Equal(0, whileTyping);
        Assert.Equal(1, afterPause);
        await Expect(page.Locator("#store [data-ui-option]")).ToHaveTextAsync(["Beretta", "Bersa"]);
    }

    // The pointer coming over the row is a render of its own, and the press that follows it at once was read
    // from the page before that render: the row is found again among the few the store answered with.
    [Fact]
    public async Task A_row_of_the_stores_answer_pressed_as_the_pointer_arrives_is_the_row_picked()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await OpenListAsync(page, "store");
        await Field(page, "store").PressSequentiallyAsync("ber");
        await page.Clock.RunForAsync(Pause);
        await Expect(page.Locator("#store [data-ui-option]")).ToHaveTextAsync(["Beretta", "Bersa"]);

        await Option(page, "store", "Bersa").ClickAsync();

        await Expect(page.Locator("#picked")).ToHaveTextAsync("Bersa");
        await Expect(page.Locator("#store [data-ui-select-button]")).ToHaveAttributeAsync("aria-expanded", "false");
        // The list shut and its field emptied: the page's search follows the field, as Flux's does.
        await Expect(page.Locator("#searches")).ToHaveTextAsync("[ber][]");
    }

    [Fact]
    public async Task A_row_pressed_during_the_pause_is_the_row_picked_and_the_text_is_never_searched_for()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await OpenListAsync(page, "store");
        await Field(page, "store").PressSequentiallyAsync("ber");

        await Option(page, "store", "Walther").ClickAsync();
        await Expect(page.Locator("#picked")).ToHaveTextAsync("Walther");
        await page.Clock.RunForAsync(Pause * 3);
        await page.ClickAsync("#ready");

        // The list never changed under the press, and the pause that ended after it found the list shut.
        await Expect(page.Locator("#searches")).ToHaveTextAsync(string.Empty);
        await Expect(page.Locator("#store [data-ui-select-button]")).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(Field(page, "store")).ToHaveValueAsync(string.Empty);
        await Expect(page.Locator("#store [data-ui-option]")).ToHaveCountAsync(5);
    }

    [Fact]
    public async Task Enter_during_the_pause_picks_the_row_the_reader_sees_lit_and_the_text_is_never_searched_for()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await OpenListAsync(page, "store");
        await Field(page, "store").PressSequentiallyAsync("col");

        await Field(page, "store").PressAsync("Enter");
        await Expect(page.Locator("#picked")).ToHaveTextAsync("Glock");
        await page.Clock.RunForAsync(Pause * 3);
        await page.ClickAsync("#ready");

        await Expect(page.Locator("#searches")).ToHaveTextAsync(string.Empty);
        await Expect(page.Locator("#store [data-ui-select-button]")).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(Field(page, "store")).ToHaveValueAsync(string.Empty);
    }

    [Fact]
    public async Task Escape_during_the_pause_empties_the_field_and_the_text_is_never_searched_for()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await OpenListAsync(page, "store");
        await Field(page, "store").PressSequentiallyAsync("ber");

        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator("#store [data-ui-select-button]")).ToHaveAttributeAsync("aria-expanded", "false");
        await page.Clock.RunForAsync(Pause * 3);
        await page.ClickAsync("#ready");

        await Expect(Field(page, "store")).ToHaveValueAsync(string.Empty);
        await Expect(page.Locator("#searches")).ToHaveTextAsync(string.Empty);
    }

    [Fact]
    public async Task The_clear_button_empties_the_field_and_tells_the_page_at_once_when_it_had_heard_a_search()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await OpenListAsync(page, "store");
        await Field(page, "store").PressSequentiallyAsync("ber");
        await page.Clock.RunForAsync(Pause);
        await Expect(page.Locator("#searches")).ToHaveTextAsync("[ber]");
        await Field(page, "store").PressSequentiallyAsync("s");

        await page.Locator("#store [data-ui-select-search] button").ClickAsync();
        await Expect(page.Locator("#searches")).ToHaveTextAsync("[ber][]");
        await page.Clock.RunForAsync(Pause * 3);
        // The list is still open, and locks the page behind it: a key in it is the round trip that follows.
        await Field(page, "store").PressAsync("ArrowDown");
        await Expect(page.Locator("#store [data-ui-option][data-active]")).ToHaveTextAsync("Beretta");

        await Expect(page.Locator("#searches")).ToHaveTextAsync("[ber][]");
        await Expect(Field(page, "store")).ToHaveValueAsync(string.Empty);
        await Expect(page.Locator("#store [data-ui-option]")).ToHaveCountAsync(5);
    }

    [Fact]
    public async Task The_arrows_walk_the_list_as_drawn_during_the_pause_and_the_search_still_arrives_at_its_end()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await OpenListAsync(page, "store");
        await Field(page, "store").PressSequentiallyAsync("ber");

        await Field(page, "store").PressAsync("ArrowDown");
        await Expect(page.Locator("#store [data-ui-option][data-active]")).ToHaveTextAsync("Beretta");
        var searchesAtTheArrow = await page.Locator("#searches").TextContentAsync();
        await page.Clock.RunForAsync(Pause);

        Assert.Equal(string.Empty, searchesAtTheArrow);
        await Expect(page.Locator("#searches")).ToHaveTextAsync("[ber]");
        await Expect(page.Locator("#store [data-ui-option]")).ToHaveTextAsync(["Beretta", "Bersa"]);
    }

    [Fact]
    public async Task The_button_goes_on_saying_the_answer_when_the_store_answers_a_search_with_other_rows()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        var button = page.Locator("#store [data-ui-select-button]");
        await OpenWhileTimeRunsAsync(page, "store");
        await Option(page, "store", "Walther").ClickAsync();
        await Expect(button).ToHaveTextAsync("Walther");
        await OpenListAsync(page, "store");

        await Field(page, "store").PressSequentiallyAsync("ber");
        await page.Clock.RunForAsync(Pause);
        await Expect(page.Locator("#store [data-ui-option]")).ToHaveTextAsync(["Beretta", "Bersa"]);
        await page.Keyboard.PressAsync("Escape");

        await Expect(button).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(button).ToHaveTextAsync("Walther");
        await Expect(page.Locator("#picked")).ToHaveTextAsync("Walther");
    }

    [Fact]
    public async Task A_list_the_select_filters_hides_rows_as_the_reader_types_and_tells_the_page_at_the_pause()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await OpenListAsync(page, "kit");

        await Field(page, "kit").PressSequentiallyAsync("ber");
        await page.Clock.RunForAsync(Pause / 4);
        await Expect(page.Locator("#kit [data-ui-option]:not([data-hidden])")).ToHaveTextAsync(["Beretta", "Bersa"]);
        var heardWhileTyping = await page.Locator("#heard").TextContentAsync();
        await page.Clock.RunForAsync(Pause);

        Assert.Equal(string.Empty, heardWhileTyping);
        await Expect(page.Locator("#heard")).ToHaveTextAsync("[ber]");
    }
}

/// <summary>Two searchable listboxes whose search waits for a pause: one the page answers from a store, one the select filters itself.</summary>
public sealed partial class SelectSearchDebouncePage : Component
{
    internal const int PauseMilliseconds = 400;

    private static readonly string[] Pistols = ["Glock", "Beretta", "Walther", "Bersa", "Colt"];

    private readonly List<string> _searches = [];
    private readonly List<string> _heard = [];
    private string[] _found = Pistols;
    private string? _picked;
    private string? _narrowed;
    private int _presses;

    protected override Component? HeadAssets => Markup.Title["select search"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() => Div[
        Main[
            Div.Id("store")[
                Ui.Select.Value(_picked).OnChange(value => _picked = value).Listbox.Searchable().Filter(false).Placeholder("Choose…")[
                    Ui.SelectSearch.OnInput(Search).Debounce(PauseMilliseconds.Milliseconds),
                    _found.Select(pistol => Ui.SelectOption.Key(pistol).Value(pistol)[pistol])
                ]
            ],
            Div.Id("kit")[
                Ui.Select.Value(_narrowed).OnChange(value => _narrowed = value).Listbox.Searchable().Placeholder("Choose…")[
                    Ui.SelectSearch.OnInput(_heard.Add).Debounce(PauseMilliseconds.Milliseconds),
                    Pistols.Select(pistol => Ui.SelectOption.Key(pistol).Value(pistol)[pistol])
                ]
            ],
            P.Id("searches")[string.Concat(_searches.Select(text => $"[{text}]"))],
            P.Id("heard")[string.Concat(_heard.Select(text => $"[{text}]"))],
            P.Id("picked")[_picked ?? string.Empty]
        ],
        // Pressed after the page's time has run: the round trip that brings back whatever a late pause changed.
        Footer[Button.Id("ready").OnClick(() => _presses++)["ready"], P.Id("presses")[_presses.ToString(System.Globalization.CultureInfo.InvariantCulture)]]
    ];

    // The store's answer: the rows drawn stay the ones before it until it is here.
    private void Search(string text)
    {
        _searches.Add(text);
        _found = [.. Pistols.Where(pistol => pistol.Contains(text, StringComparison.OrdinalIgnoreCase))];
    }
}
