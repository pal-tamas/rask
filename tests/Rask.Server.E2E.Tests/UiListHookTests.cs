using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     The kit's lists of options on a live SERVER page — <c>Ui.Select</c>'s listbox, its searchable listbox and
///     its combobox, <c>Ui.Pillbox</c> and <c>Ui.Autocomplete</c> — under a pointer and a keyboard, with the
///     socket counted: moving about an open list is the browser's, and the page hears a pick.
/// </summary>
/// <remarks>
///     Flux UI's lists are custom elements whose script keeps the lit row, the filter and whether the list is
///     open; measured on its live pages (2026-10-13), a pointer swept over every row, the arrows, type-ahead,
///     a search, Escape and Tab send its server nothing. Frames are counted in the page
///     (<see cref="SentFrames" />) and the page's time is the test's (<see cref="PageClock" />).
/// </remarks>
public sealed class UiListHookTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Rows = "[role=option]:not([data-hidden])";

    // "open or shut | lit rows | the row named to assistive technology | rows showing", for the list under #id.
    private const string State = """
        id => {
            const root = document.getElementById(id);
            const popup = root.querySelector('[popover]');
            const words = el => el.textContent.trim();
            const rows = [...popup.querySelectorAll('[role=option]')].filter(row => !row.hasAttribute('data-hidden'));
            const named = [...root.querySelectorAll('[aria-activedescendant]')].map(el => document.getElementById(el.getAttribute('aria-activedescendant')));
            return [
                popup.matches(':popover-open') ? 'open' : 'shut',
                rows.filter(row => row.hasAttribute('data-active')).map(words).join('+'),
                named.map(words).join('+'),
                rows.length,
            ].join(' | ');
        }
        """;

    private async Task<HookSession> OpenAsync()
    {
        var session = await HookSession.OpenAsync<ListHookPage>(playwright, beforeLoad: async page =>
        {
            await SentFrames.Count(page);
            await PageClock.Install(page);
        });
        // A press the page answers: the socket is up, and every handler on the page is wired.
        await session.Page.ClickAsync("#ready");
        await Expect(session.Page.Locator("#ready")).ToHaveTextAsync("ready");
        // The page came with the hooks' bundle (it has lists), and the bundle has run: asked of the page itself,
        // since its clock is the test's and a wait that polls by that clock would never come round.
        Assert.True(await session.Page.EvaluateAsync<bool>("() => !!document.querySelector('script[data-rask-hooks]') && !window.__raskHookSeam.missed"));

        return session;
    }

    [Theory]
    [InlineData("room", "le")]
    [InlineData("found", "co")]
    [InlineData("typed", "co")]
    [InlineData("tags", "s")]
    [InlineData("skills", "p")]
    [InlineData("state", "ne")]
    public async Task Opening_a_list_sweeping_the_pointer_over_it_walking_it_typing_and_leaving_send_the_server_nothing(string list, string typed)
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        var trigger = page.Locator($"#{list} [data-trigger], #{list} [role=combobox]").First;
        var popup = page.Locator($"#{list} [popover]");
        var before = await SentFrames.SoFarAsync(page);

        await trigger.ClickAsync();
        await Expect(popup).ToBeVisibleAsync();
        await SweepAsync(page, list);
        foreach (var key in (string[])["ArrowDown", "ArrowDown", "ArrowDown", "ArrowUp", "End", "Home", "PageDown", "PageUp"])
        {
            await page.Keyboard.PressAsync(key);
        }

        await page.Keyboard.TypeAsync(typed);
        await page.Keyboard.PressAsync("ArrowDown");
        await page.Keyboard.PressAsync("Backspace");
        await page.Keyboard.PressAsync("Escape");
        await Expect(popup).ToBeHiddenAsync();
        await trigger.FocusAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        await Expect(popup).ToBeVisibleAsync();
        await SweepAsync(page, list);
        await page.Keyboard.PressAsync("Tab");
        await Expect(popup).ToBeHiddenAsync();
        await page.Clock.RunForAsync(2_000);

        Assert.Equal(0, await SentFrames.SoFarAsync(page) - before);
    }

    [Fact]
    public async Task The_arrows_stop_at_either_end_step_over_a_disabled_row_and_start_from_an_end_when_no_row_is_lit()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        var walked = new List<string>();

        await page.ClickAsync("#room [role=combobox]");
        walked.Add(await SeeAsync(page, "room"));
        foreach (var key in (string[])["ArrowUp", "ArrowDown", "ArrowDown", "ArrowDown", "Home", "End", "PageDown", "ArrowDown", "ArrowDown", "ArrowDown", "ArrowDown"])
        {
            await page.Keyboard.PressAsync(key);
            walked.Add(Lit(await SeeAsync(page, "room")));
        }

        await OverAsync(page, "room", "Design services");
        await page.Mouse.MoveAsync(900, 650, new MouseMoveOptions { Steps = 3 });
        var left = await SeeAsync(page, "room");
        await page.Keyboard.PressAsync("ArrowUp");

        // Flux, key for key: "Accounting" is disabled and stepped over; Home, End and the page keys are nobody's.
        Assert.Equal(
            ["open | Photography | Photography | 7", "Photography", "Design services", "Web development", "Legal services", "Legal services",
                "Legal services", "Legal services", "Consulting", "Other", "Other", "Other"],
            walked);
        Assert.Equal("open |  |  | 7", left);
        Assert.Equal("open | Other | Other | 7", await SeeAsync(page, "room"));
    }

    [Fact]
    public async Task A_row_the_pointer_lit_goes_dark_when_the_pointer_leaves_and_one_the_keys_lit_stays()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.ClickAsync("#room [role=combobox]");

        await OverAsync(page, "room", "Web development");
        var pointed = await SeeAsync(page, "room");
        await page.Keyboard.PressAsync("ArrowDown");
        await page.Mouse.MoveAsync(900, 650, new MouseMoveOptions { Steps = 3 });
        var keyed = await SeeAsync(page, "room");
        await OverAsync(page, "room", "Other");
        await page.Mouse.MoveAsync(900, 650, new MouseMoveOptions { Steps = 3 });
        var dark = await SeeAsync(page, "room");
        await page.Keyboard.PressAsync("Enter");

        Assert.Equal("open | Web development | Web development | 7", pointed);
        // The arrow went on from the pointer's row, over the disabled one, and the keys' row outlives the pointer.
        Assert.Equal("open | Legal services | Legal services | 7", keyed);
        Assert.Equal("open |  |  | 7", dark);
        // Enter with no row lit picks nothing and leaves the list open.
        Assert.Equal("open |  |  | 7", await SeeAsync(page, "room"));
        Assert.Equal("button", await page.EvaluateAsync<string>("() => document.activeElement.localName"));
    }

    [Fact]
    public async Task Letters_move_the_cursor_of_an_open_listbox_and_pick_on_a_shut_one_and_a_reopened_list_starts_on_the_pick()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.ClickAsync("#room [role=combobox]");

        await page.Keyboard.TypeAsync("le");
        var jumped = await SeeAsync(page, "room");
        await page.Keyboard.PressAsync("Escape");
        var shut = await SeeAsync(page, "room");
        // Letters typed within half a second are one prefix: "leo" is nobody's.
        await page.Clock.RunForAsync(600);
        await page.Keyboard.TypeAsync("o");
        await Expect(page.Locator("#room [role=combobox]")).ToHaveTextAsync("Other");
        var picked = await SeeAsync(page, "room");
        await page.Keyboard.PressAsync("ArrowDown");

        Assert.Equal("open | Legal services | Legal services | 7", jumped);
        Assert.Equal("shut |  |  | 7", shut);
        Assert.Equal("shut |  |  | 7", picked);
        Assert.Equal("open | Other | Other | 7", await SeeAsync(page, "room"));
    }

    [Fact]
    public async Task Typing_hides_the_rows_that_do_not_hold_it_lights_the_first_left_and_shows_the_empty_row_when_none_is()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        var typed = new List<string>();
        await page.ClickAsync("#typed [role=combobox]");

        foreach (var text in (string[])["c", "o", "z"])
        {
            await page.Keyboard.TypeAsync(text);
            typed.Add(await SeeAsync(page, "typed"));
        }

        var empty = await page.Locator("#typed [data-rask-list-empty]").IsVisibleAsync();
        await page.Keyboard.PressAsync("Backspace");
        await page.Keyboard.PressAsync("ArrowDown");
        await page.Keyboard.PressAsync("Enter");

        Assert.Equal(["open | Design services | Design services | 4", "open | Accounting | Accounting | 2", "open |  |  | 0"], typed);
        Assert.True(empty);
        await Expect(page.Locator("#typed [role=combobox]")).ToHaveValueAsync("Consulting");
        Assert.Equal("shut |  |  | 7", await SeeAsync(page, "typed"));
    }

    [Fact]
    public async Task A_combobox_left_with_words_that_name_nothing_says_its_answer_again()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        var input = page.Locator("#typed [role=combobox]");
        await input.ClickAsync();
        await page.Keyboard.TypeAsync("de");
        await page.Keyboard.PressAsync("Enter");
        await Expect(input).ToHaveValueAsync("Design services");

        await page.Keyboard.TypeAsync("xyz");
        var typing = await input.InputValueAsync();
        await page.Keyboard.PressAsync("Escape");
        var escaped = await input.InputValueAsync();
        await page.Keyboard.PressAsync("Escape");
        var emptied = await input.InputValueAsync();
        await page.Keyboard.PressAsync("Tab");

        Assert.Equal("Design servicesxyz", typing);
        // Flux's `strict`: Escape shuts the list and the answer is back; a second Escape takes the words, not the answer.
        Assert.Equal("Design services", escaped);
        Assert.Equal(string.Empty, emptied);
        await Expect(input).ToHaveValueAsync("Design services");
    }

    [Fact]
    public async Task What_the_pointer_and_the_keys_wrote_survives_a_render_the_server_sends_meanwhile()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.ClickAsync("#typed [role=combobox]");
        await page.Keyboard.TypeAsync("e");
        await page.Keyboard.PressAsync("ArrowDown");
        var before = await SeeAsync(page, "typed");

        // A press the page hears, from script so the list stays open: it renders the whole page again.
        await page.EvaluateAsync("() => document.getElementById('ready').click()");
        await Expect(page.Locator("#renders")).ToHaveTextAsync("renders=2");

        Assert.Equal("open | Web development | Web development | 4", before);
        Assert.Equal(before, await SeeAsync(page, "typed"));
        await Expect(page.Locator("#typed [role=combobox]")).ToHaveAttributeAsync("aria-expanded", "true");
    }

    [Theory]
    [InlineData("room", "Legal services")]
    [InlineData("found", "Legal services")]
    [InlineData("tags", "Ruby")]
    public async Task A_pick_is_the_one_press_the_page_hears_and_the_row_pressed_is_the_row_picked(string list, string row)
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.Locator($"#{list} [role=combobox]").First.ClickAsync();
        await OverAsync(page, list, row);
        var before = await SentFrames.SoFarAsync(page);

        await page.Locator($"#{list} [role=option]").Filter(new() { HasText = row }).ClickAsync();
        await Expect(page.Locator($"#{list} [role=option][aria-selected=true]")).ToHaveTextAsync(row);
        await page.Clock.RunForAsync(1_000);

        Assert.Equal(1, await SentFrames.SoFarAsync(page) - before);
    }

    [Fact]
    public async Task An_autocomplete_bound_to_a_member_that_waits_sends_nothing_at_a_pick_and_its_text_with_the_next_action()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        var input = page.Locator("#state [role=combobox]");
        var before = await SentFrames.SoFarAsync(page);

        await input.ClickAsync();
        await page.Keyboard.TypeAsync("ne");
        await page.Keyboard.PressAsync("ArrowDown");
        await page.Keyboard.PressAsync("Enter");
        await Expect(input).ToHaveValueAsync("Nevada");
        await Expect(input).ToBeFocusedAsync();
        await page.Clock.RunForAsync(1_000);
        var atPick = await SentFrames.SoFarAsync(page) - before;
        await page.ClickAsync("#save");

        Assert.Equal(0, atPick);
        await Expect(page.Locator("#model")).ToContainTextAsync("|Nevada");
    }

    private static string Lit(string state) => state.Split(" | ")[1];

    // What the list shows now. The runtime answers in the key's own task, so there is nothing to wait for.
    private static Task<string> SeeAsync(IPage page, string list) => page.EvaluateAsync<string>(State, list);

    // A real pointer onto the row's middle, in steps: a jump would enter nothing on the way.
    private static async Task OverAsync(IPage page, string list, string words)
    {
        var box = (await page.Locator($"#{list} [role=option]").Filter(new() { HasText = words }).BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(box.X + (box.Width / 2), box.Y + (box.Height / 2), new MouseMoveOptions { Steps = 3 });
    }

    // A real pointer onto the middle of every row that shows, in steps: a jump would enter nothing on the way.
    private static async Task SweepAsync(IPage page, string list)
    {
        var rows = page.Locator($"#{list} {Rows}");
        var count = await rows.CountAsync();
        for (var i = 0; i < count; i++)
        {
            await rows.Nth(i).ScrollIntoViewIfNeededAsync();
            var box = (await rows.Nth(i).BoundingBoxAsync())!;
            await page.Mouse.MoveAsync(box.X + (box.Width / 2), box.Y + (box.Height / 2), new MouseMoveOptions { Steps = 3 });
        }
    }
}

/// <summary>Every kind of list the kit draws, each bound to a member that waits for the next action.</summary>
public sealed partial class ListHookPage : Component
{
    private static readonly string[] Industries =
        ["Photography", "Design services", "Web development", "Accounting", "Legal services", "Consulting", "Other"];

    private static readonly string[] Skills = ["PHP", "JavaScript", "Python", "Ruby", "Go", "Rust", "Swift"];

    private static readonly string[] States = ["Alabama", "Nebraska", "Nevada", "New Hampshire", "New Jersey", "Ohio"];

    private readonly Answers _answers = new();
    private bool _ready;
    private int _saves;
    private int _renders;

    // The kit's own sheet: it is what hides a row a search left out.
    protected override Component? HeadAssets => [Markup.Title["list hooks"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        Button.Id("ready").OnClick(() => { _ready = true; _renders++; })[_ready ? "ready" : "loading"],
        P.Id("renders")[$"renders={_renders}"],
        P.Id("model")[
            $"{_answers.Room}|{_answers.Found}|{_answers.Typed}|{string.Join(',', _answers.Tags)}|{string.Join(',', _answers.Skills)}|{_answers.State}"
        ],
        P.Id("saves")[$"saves={_saves}"],
        Form.Model(_answers).OnSubmit(() => _saves++)[
            Div.Id("room").Class("max-w-64")[
                Ui.Select.Bind(() => _answers.Room).Listbox.Placeholder("Choose…")[
                    Industries.Select(name => Ui.SelectOption.Key(name).Disabled(name == "Accounting")[name])
                ]
            ],
            Div.Id("found").Class("max-w-64")[
                Ui.Select.Bind(() => _answers.Found).Listbox.Searchable().Placeholder("Choose…")[
                    Industries.Select(name => Ui.SelectOption.Key(name)[name])
                ]
            ],
            Div.Id("typed").Class("max-w-64")[
                Ui.Select.Bind(() => _answers.Typed).Combobox.Placeholder("Choose…")[
                    Industries.Select(name => Ui.SelectOption.Key(name)[name])
                ]
            ],
            Div.Id("tags").Class("max-w-64")[
                Ui.Pillbox.Bind(() => _answers.Tags).Placeholder("Choose…")[
                    Skills.Select(name => Ui.PillboxOption.Key(name)[name])
                ]
            ],
            Div.Id("skills").Class("max-w-64")[
                Ui.Pillbox.Bind(() => _answers.Skills).Combobox.Placeholder("Choose…")[
                    Skills.Select(name => Ui.PillboxOption.Key(name)[name])
                ]
            ],
            Div.Id("state").Class("max-w-64")[
                Ui.Autocomplete.Bind(() => _answers.State)[States.Select(state => Ui.AutocompleteItem.Key(state)[state])]
            ],
            Ui.Button.Primary.Submit.Id("save")["Save"]
        ],
        Div.Style("height:1200px")
    ];

    private sealed class Answers
    {
        public string Room { get; set; } = "";

        public string Found { get; set; } = "";

        public string Typed { get; set; } = "";

        public List<string> Tags { get; set; } = [];

        public List<string> Skills { get; set; } = [];

        public string State { get; set; } = "";
    }
}
