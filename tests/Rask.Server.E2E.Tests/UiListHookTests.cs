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

    // The kit's own sheet: it is what hides a row a search left out.
    protected override Component? HeadAssets => [Markup.Title["list hooks"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        Button.Id("ready").OnClick(() => _ready = true)[_ready ? "ready" : "loading"],
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
