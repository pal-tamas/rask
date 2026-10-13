using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     The <c>Debounce</c> step of the fields that ask a store beside <c>Ui.SelectSearch</c> — the combobox's
///     <c>Ui.SelectInput</c>, <c>Ui.PillboxSearch</c>, <c>Ui.PillboxInput</c>, <c>Ui.Autocomplete</c> — in a real
///     browser over the real socket: one search for a word, and a pick made during the pause is the row pressed.
/// </summary>
/// <remarks>
///     The pause is counted by the page's own timers, so the page's clock is the test's (<see cref="PageClock" />).
/// </remarks>
public sealed class SearchDebounceTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const int Pause = SearchDebouncePage.PauseMilliseconds;

    // Long enough for a frame and a timer of none, and nowhere near the pause.
    private const int Settle = 20;

    private static ILocator Row(IPage page, string control, string name) =>
        page.Locator($"#{control} [role=option]:not([data-hidden])").Filter(new() { HasText = name });

    private SocketFrames? _trace;

    private async Task Traced(Func<Task> act)
    {
        try
        {
            await act();
        }
        catch (PlaywrightException e)
        {
            throw new InvalidOperationException(e.Message.Split('\n')[0] + Environment.NewLine + "TRACE" + Environment.NewLine + _trace!.Trace());
        }
    }

    private async Task<HookSession> OpenAsync(Action<RaskServerOptions>? server = null)
    {
        var session = await HookSession.OpenAsync<SearchDebouncePage>(playwright, server: server, beforeLoad: async page =>
        {
            await SentFrames.Count(page);
            _trace = SocketFrames.Of(page);
            await HeldReplies.Install(page);
            await PageClock.Install(page);
        });
        await Expect(session.Page.Locator("#ready")).ToHaveTextAsync("ready");

        return session;
    }

    // The control's list open and its field focused, with the page's time stopped from then on.
    private static async Task<ILocator> OpenListAsync(IPage page, string control, string press, string field)
    {
        await page.ClickAsync($"#{control} {press}");
        await Expect(page.Locator($"#{control} [popover]")).ToHaveAttributeAsync("data-rask-popover-open", "true");
        await Expect(page.Locator($"#{control} {field}")).ToBeFocusedAsync();
        await PageClock.StopAsync(page);
        // What the open list still owed the page — its place, the lock behind it — is a frame or a timer of none.
        await page.Clock.RunForAsync(Settle);

        return page.Locator($"#{control} {field}");
    }

    private static Task<ILocator> OpenComboboxAsync(IPage page) => OpenListAsync(page, "combo", "input[role=combobox]", "input[role=combobox]");

    private static Task<ILocator> OpenSearchedPillsAsync(IPage page) => OpenListAsync(page, "pills", "[data-ui-pillbox-trigger]", "[data-ui-pillbox-search] input");

    private static Task<ILocator> OpenTypedPillsAsync(IPage page) => OpenListAsync(page, "typed", "[data-ui-pillbox-trigger]", "[data-ui-pillbox-input]");

    // What the page was asked, once the round trip a late pause could have made is back.
    private static async Task<ILocator> SearchesAfterAsync(IPage page, string control)
    {
        await page.Clock.RunForAsync(Pause * 3);
        await RoundTripAsync(page);

        return page.Locator($"#{control}-searches");
    }

    // A round trip that brings back whatever went before it. Dispatched, not pressed: a list that stays open
    // keeps the pointer from the page behind it.
    private static async Task RoundTripAsync(IPage page)
    {
        var presses = await page.Locator("#presses").TextContentAsync();
        await page.Locator("#ready").DispatchEventAsync("click");
        await Expect(page.Locator("#presses")).Not.ToHaveTextAsync(presses!);
    }

    // ----- Ui.SelectInput ---------------------------------------------------------------------------------

    [Fact]
    public async Task Typing_into_an_open_combobox_sends_nothing_until_the_pause_and_then_the_final_text_once()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        var field = await OpenComboboxAsync(page);
        var before = await SentFrames.SoFarAsync(page);

        await field.PressSequentiallyAsync("ber");
        await page.Clock.RunForAsync(Pause - 1);
        var whileTyping = await SentFrames.SoFarAsync(page) - before;
        await page.Clock.RunForAsync(1);
        await Expect(page.Locator("#combo-searches")).ToHaveTextAsync("[ber]");
        await page.Clock.RunForAsync(Pause * 3);
        var afterPause = await SentFrames.SoFarAsync(page) - before;

        Assert.Equal(0, whileTyping);
        Assert.Equal(1, afterPause);
        await Expect(page.Locator("#combo [role=option]")).ToHaveTextAsync(["Beretta", "Bersa"]);
    }

    [Fact]
    public async Task The_first_key_in_a_closed_combobox_opens_its_list_and_the_store_is_asked_once_at_the_pause()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.Locator("#combo input[role=combobox]").FocusAsync();

        await page.Keyboard.TypeAsync("ber");
        await Expect(page.Locator("#combo [popover]")).ToHaveAttributeAsync("data-rask-popover-open", "true");
        await Expect(page.Locator("#combo-searches")).ToHaveTextAsync("[ber]");

        await Expect(page.Locator("#combo [role=option]")).ToHaveTextAsync(["Beretta", "Bersa"]);
        await Expect(page.Locator("#combo input[role=combobox]")).ToHaveValueAsync("ber");
    }

    [Fact]
    public async Task A_row_pressed_during_a_comboboxs_pause_is_the_row_picked_and_the_text_is_never_searched_for()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        var field = await OpenComboboxAsync(page);
        await field.PressSequentiallyAsync("ber");
        await Row(page, "combo", "Walther").ClickAsync();
        await Traced(() => Expect(page.Locator("#combo-picked")).ToHaveTextAsync("Walther"));
        var searches = await SearchesAfterAsync(page, "combo");

        // The input says its answer, and the page's search follows the input, as Flux's does.
        await Expect(searches).ToHaveTextAsync("[Walther]");
        await Expect(field).ToHaveValueAsync("Walther");
        await Expect(page.Locator("#combo [popover]")).ToHaveAttributeAsync("data-rask-popover-open", "false");
    }

    [Fact]
    public async Task Escape_during_a_comboboxs_pause_puts_its_answer_back_and_the_text_is_never_searched_for()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        var field = await OpenComboboxAsync(page);
        await field.PressSequentiallyAsync("ber");

        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator("#combo [popover]")).ToHaveAttributeAsync("data-rask-popover-open", "false");
        var searches = await SearchesAfterAsync(page, "combo");

        await Expect(searches).ToHaveTextAsync(string.Empty);
        await Expect(field).ToHaveValueAsync(string.Empty);
    }

    [Fact]
    public async Task The_create_row_is_handed_what_the_field_holds_when_it_is_pressed_not_what_the_page_last_heard()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        var field = await OpenComboboxAsync(page);
        await field.PressSequentiallyAsync("lug");
        await page.Clock.RunForAsync(Pause);
        await Expect(page.Locator("#combo-searches")).ToHaveTextAsync("[lug]");
        await field.PressSequentiallyAsync("er");

        await page.Locator("#combo [data-ui-option-create]").ClickAsync();
        await page.Clock.RunForAsync(Settle);

        await Expect(page.Locator("#combo-picked")).ToHaveTextAsync("luger");
    }

    // ----- Ui.PillboxSearch ---------------------------------------------------------------------------------

    [Fact]
    public async Task Typing_into_a_pillboxs_search_sends_nothing_until_the_pause_and_then_the_final_text_once()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        var field = await OpenSearchedPillsAsync(page);
        var before = await SentFrames.SoFarAsync(page);

        await field.PressSequentiallyAsync("ber");
        await page.Clock.RunForAsync(Pause - 1);
        var whileTyping = await SentFrames.SoFarAsync(page) - before;
        await page.Clock.RunForAsync(1);
        await Expect(page.Locator("#pills-searches")).ToHaveTextAsync("[ber]");
        await page.Clock.RunForAsync(Pause * 3);
        var afterPause = await SentFrames.SoFarAsync(page) - before;

        Assert.Equal(0, whileTyping);
        Assert.Equal(1, afterPause);
        await Expect(page.Locator("#pills [role=option]")).ToHaveTextAsync(["Beretta", "Bersa"]);
    }

    [Fact]
    public async Task A_row_pressed_during_a_pillbox_searchs_pause_is_the_pill_added_and_the_text_is_never_searched_for()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        var field = await OpenSearchedPillsAsync(page);
        await field.PressSequentiallyAsync("ber");

        await Row(page, "pills", "Walther").ClickAsync();
        await Expect(page.Locator("#pills-picked")).ToHaveTextAsync("Walther");
        var searches = await SearchesAfterAsync(page, "pills");

        await Expect(searches).ToHaveTextAsync(string.Empty);
        await Expect(field).ToHaveValueAsync(string.Empty);
        await Expect(page.Locator("#pills [role=option]")).ToHaveCountAsync(5);
    }

    [Fact]
    public async Task Escape_and_the_clear_button_empty_a_pillboxs_search_and_only_a_heard_search_is_taken_back()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        var field = await OpenSearchedPillsAsync(page);
        await field.PressSequentiallyAsync("ber");
        await page.Clock.RunForAsync(Pause);
        await Expect(page.Locator("#pills-searches")).ToHaveTextAsync("[ber]");
        await field.PressSequentiallyAsync("s");

        await page.Locator("#pills [data-ui-pillbox-search] button").ClickAsync();
        await Expect(page.Locator("#pills-searches")).ToHaveTextAsync("[ber][]");
        await field.PressSequentiallyAsync("col");
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator("#pills [popover]")).ToHaveAttributeAsync("data-rask-popover-open", "false");
        var searches = await SearchesAfterAsync(page, "pills");

        await Expect(searches).ToHaveTextAsync("[ber][]");
        await Expect(field).ToHaveValueAsync(string.Empty);
    }

    // ----- Ui.PillboxInput ----------------------------------------------------------------------------------

    [Fact]
    public async Task Typing_into_a_pillboxs_input_sends_nothing_until_the_pause_and_then_the_final_text_once()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        var field = await OpenTypedPillsAsync(page);
        var before = await SentFrames.SoFarAsync(page);

        await field.PressSequentiallyAsync("ber");
        await page.Clock.RunForAsync(Pause - 1);
        var whileTyping = await SentFrames.SoFarAsync(page) - before;
        await page.Clock.RunForAsync(1);
        await Expect(page.Locator("#typed-searches")).ToHaveTextAsync("[ber]");
        await page.Clock.RunForAsync(Pause * 3);
        var afterPause = await SentFrames.SoFarAsync(page) - before;

        Assert.Equal(0, whileTyping);
        Assert.Equal(1, afterPause);
        await Expect(page.Locator("#typed [role=option]")).ToHaveTextAsync(["Beretta", "Bersa"]);
    }

    [Fact]
    public async Task Enter_during_a_pillbox_inputs_pause_adds_the_lit_row_and_the_text_is_never_searched_for()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        var field = await OpenTypedPillsAsync(page);
        await field.PressSequentiallyAsync("ber");

        await field.PressAsync("Enter");
        await Expect(page.Locator("#typed-picked")).ToHaveTextAsync("Glock");
        var searches = await SearchesAfterAsync(page, "typed");

        await Expect(searches).ToHaveTextAsync(string.Empty);
        await Expect(field).ToHaveValueAsync(string.Empty);
    }

    [Fact]
    public async Task Backspace_takes_a_letter_off_what_is_typed_and_a_pill_off_only_from_an_empty_input()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        var field = await OpenTypedPillsAsync(page);
        await field.PressAsync("Enter");
        await Expect(page.Locator("#typed-picked")).ToHaveTextAsync("Glock");
        await field.PressSequentiallyAsync("be");

        await field.PressAsync("Backspace");
        await field.PressAsync("Backspace");
        await RoundTripAsync(page);
        var afterTheLetters = await page.Locator("#typed-picked").TextContentAsync();
        await field.FocusAsync();
        await field.PressAsync("Backspace");

        Assert.Equal("Glock", afterTheLetters);
        await Expect(page.Locator("#typed-picked")).ToHaveTextAsync(string.Empty);
    }

    [Fact]
    public async Task Escape_during_a_pillbox_inputs_pause_empties_it_and_the_text_is_never_searched_for()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        var field = await OpenTypedPillsAsync(page);
        await field.PressSequentiallyAsync("ber");

        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator("#typed [popover]")).ToHaveAttributeAsync("data-rask-popover-open", "false");
        var searches = await SearchesAfterAsync(page, "typed");

        await Expect(searches).ToHaveTextAsync(string.Empty);
        await Expect(field).ToHaveValueAsync(string.Empty);
    }

    // ----- Ui.Autocomplete ----------------------------------------------------------------------------------

    [Fact]
    public async Task An_autocomplete_filters_as_the_reader_types_and_hands_the_page_the_text_once_at_the_pause()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        var field = page.Locator("#auto input[role=combobox]");
        await field.FocusAsync();
        await PageClock.StopAsync(page);

        await field.PressSequentiallyAsync("ber");
        await page.Clock.RunForAsync(Pause / 4);
        await Expect(page.Locator("#auto [role=option]:not([data-hidden])")).ToHaveTextAsync(["Beretta", "Bersa"]);
        var heardWhileTyping = await page.Locator("#auto-searches").TextContentAsync();
        await page.Clock.RunForAsync(Pause);

        Assert.Equal(string.Empty, heardWhileTyping);
        await Expect(page.Locator("#auto-searches")).ToHaveTextAsync("[ber]");
    }

    [Fact]
    public async Task An_item_pressed_during_an_autocompletes_pause_is_the_text_and_what_was_typed_is_never_handed_over()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        var field = page.Locator("#auto input[role=combobox]");
        await field.FocusAsync();
        await PageClock.StopAsync(page);
        await field.PressSequentiallyAsync("ber");
        await page.Clock.RunForAsync(Pause / 4);

        await Row(page, "auto", "Bersa").ClickAsync();
        await Traced(() => Expect(page.Locator("#auto-text")).ToHaveTextAsync("Bersa"));
        var searches = await SearchesAfterAsync(page, "auto");

        await Expect(searches).ToHaveTextAsync("[Bersa]");
        await Expect(field).ToHaveValueAsync("Bersa");
    }

    [Fact]
    public async Task Explore_A_auto_all_typed_answered_then_press_held()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        var field = page.Locator("#auto input[role=combobox]");
        await field.FocusAsync();
        await PageClock.StopAsync(page);
        await field.PressSequentiallyAsync("ber");
        await page.Clock.RunForAsync(Pause / 4);
        await Expect(page.Locator("#auto [role=option]:not([data-hidden])")).ToHaveTextAsync(["Beretta", "Bersa"]);

        await HeldReplies.HoldAsync(page);
        await Row(page, "auto", "Bersa").ClickAsync();
        await HeldReplies.ReleaseAsync(page);

        await Expect(page.Locator("#auto-text")).ToHaveTextAsync("Bersa");
    }

    [Fact]
    public async Task Explore_B_auto_first_key_answered_rest_and_press_held()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        var field = page.Locator("#auto input[role=combobox]");
        await field.FocusAsync();
        await PageClock.StopAsync(page);
        await field.PressSequentiallyAsync("b");
        await page.Clock.RunForAsync(Pause / 4);
        await Expect(page.Locator("#auto [role=option]:not([data-hidden])")).ToHaveTextAsync(["Beretta", "Bersa"]);

        await HeldReplies.HoldAsync(page);
        await field.PressSequentiallyAsync("er");
        await Row(page, "auto", "Bersa").ClickAsync();
        await HeldReplies.ReleaseAsync(page);

        await Expect(page.Locator("#auto-text")).ToHaveTextAsync("Bersa");
    }

    [Fact]
    public async Task Explore_D_auto_typed_answers_land_after_the_clock_ran()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        var field = page.Locator("#auto input[role=combobox]");
        await field.FocusAsync();
        await PageClock.StopAsync(page);
        await HeldReplies.HoldAsync(page);
        await field.PressSequentiallyAsync("ber");
        await page.Clock.RunForAsync(Pause / 4);
        await HeldReplies.ReleaseAsync(page);

        await Row(page, "auto", "Bersa").ClickAsync();

        await Expect(page.Locator("#auto-text")).ToHaveTextAsync("Bersa");
    }

    [Fact]
    public async Task Explore_E_auto_typed_answers_and_press_all_held()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        var field = page.Locator("#auto input[role=combobox]");
        await field.FocusAsync();
        await PageClock.StopAsync(page);
        await HeldReplies.HoldAsync(page);
        await field.PressSequentiallyAsync("ber");
        await page.Clock.RunForAsync(Pause / 4);
        await HeldReplies.ReleaseAsync(page);
        await Expect(Row(page, "auto", "Bersa")).ToBeVisibleAsync();
        await HeldReplies.HoldAsync(page);
        await Row(page, "auto", "Bersa").ClickAsync();
        await HeldReplies.ReleaseAsync(page);

        await Expect(page.Locator("#auto-text")).ToHaveTextAsync("Bersa");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public async Task Explore_F_auto_no_repaint_budget(int run)
    {
        Assert.True(run >= 0);
        await using var session = await OpenAsync(options => options.HandlerQuiescenceTimeout = TimeSpan.Zero);
        var page = session.Page;
        var field = page.Locator("#auto input[role=combobox]");
        await field.FocusAsync();
        await PageClock.StopAsync(page);
        await field.PressSequentiallyAsync("ber");
        await page.Clock.RunForAsync(Pause / 4);

        await Row(page, "auto", "Bersa").ClickAsync();

        await Traced(() => Expect(page.Locator("#auto-text")).ToHaveTextAsync("Bersa"));
    }

    public static TheoryData<int> Delays => [.. Enumerable.Range(0, 40).Select(i => 100 + (i * 3))];

    [Theory]
    [MemberData(nameof(Delays))]
    public async Task Explore_G_auto_answer_lands_as_the_budget_ends(int delay)
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        var field = page.Locator("#auto input[role=combobox]");
        await field.FocusAsync();
        await PageClock.StopAsync(page);
        await field.PressSequentiallyAsync("ber");
        await page.Clock.RunForAsync(Pause / 4);
        await Expect(Row(page, "auto", "Bersa")).ToBeVisibleAsync();
        await Row(page, "auto", "Bersa").HoverAsync();
        await RoundTripAsync(page);

        await HeldReplies.HoldAsync(page);
        await Row(page, "auto", "Bersa").ClickAsync();
        await Task.Delay(delay, TestContext.Current.CancellationToken);
        await HeldReplies.ReleaseAsync(page);

        await Traced(() => Expect(page.Locator("#auto-text")).ToHaveTextAsync("Bersa", new() { Timeout = 2000 }));
    }

    [Fact]
    public async Task Explore_C_combo_press_held()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        var field = await OpenComboboxAsync(page);
        await field.PressSequentiallyAsync("ber");

        await HeldReplies.HoldAsync(page);
        await Row(page, "combo", "Walther").ClickAsync();
        await HeldReplies.ReleaseAsync(page);

        await Expect(page.Locator("#combo-picked")).ToHaveTextAsync("Walther");
    }

    [Fact]
    public async Task Escape_and_the_clear_button_empty_an_autocomplete_and_the_page_holds_nothing_typed_before_them()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        var field = page.Locator("#auto input[role=combobox]");
        await field.FocusAsync();
        await PageClock.StopAsync(page);
        await field.PressSequentiallyAsync("ber");
        await page.Clock.RunForAsync(Pause / 4);

        await page.Keyboard.PressAsync("Escape");
        await page.Clock.RunForAsync(Pause * 3);
        await field.PressSequentiallyAsync("col");
        await page.Clock.RunForAsync(Pause * 2);
        await Expect(page.Locator("#auto-searches")).ToHaveTextAsync("[col]");
        // Dispatched, not pressed: the open list keeps the pointer from everything behind it.
        await page.Locator("#auto [data-ui-clear-button]").DispatchEventAsync("click");
        var searches = await SearchesAfterAsync(page, "auto");

        await Expect(searches).ToHaveTextAsync("[col][]");
        await Expect(field).ToHaveValueAsync(string.Empty);
    }
}

/// <summary>The four fields whose search waits for a pause, each over a store the page answers from.</summary>
public sealed partial class SearchDebouncePage : Component
{
    internal const int PauseMilliseconds = 400;

    private static readonly string[] Pistols = ["Glock", "Beretta", "Walther", "Bersa", "Colt"];

    private readonly List<string> _comboSearches = [];
    private readonly List<string> _pillSearches = [];
    private readonly List<string> _typedSearches = [];
    private readonly List<string> _autoSearches = [];
    private readonly List<string> _store = [.. Pistols];
    private List<string> _combo = [.. Pistols];
    private string[] _pills = Pistols;
    private string[] _typed = Pistols;
    private string[] _auto = Pistols;
    private string? _picked;
    private ICollection<string> _pillsPicked = [];
    private ICollection<string> _typedPicked = [];
    private string _text = string.Empty;
    private int _presses;

    // The kit's sheet: a list is pressed where it is drawn, and without it a row is as tall as its icon's box.
    protected override Component? HeadAssets => [Markup.Title["search debounce"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override string? HtmlLang => "en";

    protected override Component? Render() => Div[
        Main[
            Div.Id("combo")[
                Ui.Select.Value(_picked).OnChange(value => _picked = value).Combobox.Filter(false).Placeholder("Choose…")[
                    Ui.SelectInput.OnInput(text => _combo = Asked(_comboSearches, text, _store)).Debounce(PauseMilliseconds.Milliseconds),
                    _combo.Select(pistol => Ui.SelectOption.Key(pistol).Value(pistol)[pistol]),
                    Ui.SelectOptionCreate.MinLength(2).OnClick(Create)["Create new"]
                ]
            ],
            Div.Id("pills")[
                Ui.Pillbox.Values(_pillsPicked).OnChange(values => _pillsPicked = values).Filter(false).Placeholder("Choose…")[
                    Ui.PillboxSearch.OnInput(text => _pills = [.. Asked(_pillSearches, text, Pistols)]).Debounce(PauseMilliseconds.Milliseconds),
                    _pills.Select(pistol => Ui.PillboxOption.Key(pistol).Value(pistol)[pistol])
                ]
            ],
            Div.Id("typed")[
                Ui.Pillbox.Values(_typedPicked).OnChange(values => _typedPicked = values).Combobox.Filter(false)[
                    Ui.PillboxInput.OnInput(text => _typed = [.. Asked(_typedSearches, text, Pistols)]).Debounce(PauseMilliseconds.Milliseconds).Placeholder("Choose…"),
                    _typed.Select(pistol => Ui.PillboxOption.Key(pistol).Value(pistol)[pistol])
                ]
            ],
            Div.Id("auto")[
                Ui.Autocomplete.Value(_text).OnChange(Suggest).Debounce(PauseMilliseconds.Milliseconds).Clearable()[
                    _auto.Select(pistol => Ui.AutocompleteItem.Key(pistol)[pistol])
                ]
            ],
            Said("combo-searches", _comboSearches),
            Said("pills-searches", _pillSearches),
            Said("typed-searches", _typedSearches),
            Said("auto-searches", _autoSearches),
            P.Id("combo-picked")[_picked ?? string.Empty],
            P.Id("pills-picked")[string.Join(",", _pillsPicked)],
            P.Id("typed-picked")[string.Join(",", _typedPicked)],
            P.Id("auto-text")[_text]
        ],
        // Pressed after the page's time has run: the round trip that brings back whatever a late pause changed.
        Footer[Button.Id("ready").OnClick(() => _presses++)["ready"], P.Id("presses")[_presses.ToString(System.Globalization.CultureInfo.InvariantCulture)]]
    ];

    private static Component Said(string id, List<string> searches) =>
        P.Id(id)[string.Concat(searches.Select(text => $"[{text}]"))];

    // The store's answer: the rows drawn stay the ones before it until it is here.
    private static List<string> Asked(List<string> searches, string text, IEnumerable<string> store)
    {
        searches.Add(text);

        return [.. store.Where(pistol => pistol.Contains(text, StringComparison.OrdinalIgnoreCase))];
    }

    private void Create(string name)
    {
        _store.Add(name);
        _combo = [.. _store];
        _picked = name;
    }

    private void Suggest(string text)
    {
        _text = text;
        _autoSearches.Add(text);
        _auto = [.. Pistols.Where(pistol => pistol.Contains(text, StringComparison.OrdinalIgnoreCase))];
    }
}
