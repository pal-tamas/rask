using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     <c>Ui.Tabs</c> on a live SERVER page: a tab is selected, and its panel shown, in the browser — as Flux's
///     script does it — and the server hears only the row's value, when its binding says, or a callback the page
///     gave it.
/// </summary>
/// <remarks>
///     The socket is watched in both directions. The last two tests are the long form both apps build as tabs:
///     fields typed on a tab that is no longer shown stay in the form and are submitted, and a save that is
///     refused opens the tab with the first mistake — also when that is the tab the page rendered first, which
///     no diff would say.
/// </remarks>
public sealed class UiTabsBrowserTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Fact]
    public async Task A_tab_pressed_or_reached_by_an_arrow_is_selected_in_the_browser_and_sends_the_server_nothing()
    {
        await using var watched = await Watched.OpenAsync(playwright);
        var page = watched.Session.Page;

        await page.ClickAsync("#own [role=tab] >> nth=1");
        await Expect(page.Locator("#own [role=tabpanel]:not([hidden])")).ToHaveTextAsync("Your email.");
        await page.Keyboard.PressAsync("ArrowRight");
        await page.Keyboard.PressAsync("ArrowRight");

        // A press and two arrows, the second around the end: not one frame, and everything a reader of the markup reads moved.
        Assert.Equal((0, 0), watched.Frames());
        Assert.Equal("true false false|0 -1 -1|profile", await page.EvaluateAsync<string>(Row, "own"));
        await Expect(page.Locator("#own [role=tabpanel][data-selected][tabindex='0']")).ToHaveTextAsync("Your name.");
        await Expect(page.Locator("#own [role=tabpanel][hidden]")).ToHaveCountAsync(2);
    }

    [Fact]
    public async Task The_tab_the_reader_chose_stands_when_the_page_is_rendered_again()
    {
        await using var watched = await Watched.OpenAsync(playwright);
        var page = watched.Session.Page;
        await page.ClickAsync("#own [role=tab] >> nth=2");

        await page.ClickAsync("#tick");

        await Expect(page.Locator("#tick")).ToHaveTextAsync("Ticked 1");
        await Expect(page.Locator("#own [role=tab] >> nth=2")).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(page.Locator("#own [role=tabpanel]:not([hidden])")).ToHaveTextAsync("Your plan.");
    }

    [Fact]
    public async Task A_bound_row_says_nothing_until_the_readers_next_action_and_then_the_page_has_the_tab()
    {
        await using var watched = await Watched.OpenAsync(playwright);
        var page = watched.Session.Page;

        await page.ClickAsync("#bound [role=tab] >> nth=2");
        await page.ClickAsync("#bound [role=tab] >> nth=1");
        var before = watched.Frames();
        await page.ClickAsync("#tick");

        Assert.Equal((0, 0), before);
        await Expect(page.Locator("#bound-says")).ToHaveTextAsync("board");
        await Expect(page.Locator("#bound [role=tab] >> nth=1")).ToHaveAttributeAsync("data-selected", "");
    }

    [Fact]
    public async Task A_live_row_and_one_the_page_listens_to_tell_it_at_once_with_one_frame_a_choice()
    {
        await using var watched = await Watched.OpenAsync(playwright);
        var page = watched.Session.Page;

        await page.ClickAsync("#live [role=tab] >> nth=1");
        await Expect(page.Locator("#live-says")).ToHaveTextAsync("board");
        var live = watched.Frames().Sent;
        await page.ClickAsync("#heard [role=tab] >> nth=1");
        await Expect(page.Locator("#heard-says")).ToHaveTextAsync("board");

        Assert.Equal(1, live);
        Assert.Equal(2, watched.Frames().Sent);
        await Expect(page.Locator("#heard [role=tab] >> nth=1")).ToHaveAttributeAsync("aria-selected", "true");
    }

    [Fact]
    public async Task A_page_that_does_not_take_the_tab_the_reader_chose_has_the_last_word()
    {
        await using var watched = await Watched.OpenAsync(playwright);
        var page = watched.Session.Page;

        await page.ClickAsync("#heard [role=tab] >> nth=2");

        await Expect(page.Locator("#heard-says")).ToHaveTextAsync("list, refused 1");
        await Expect(page.Locator("#heard [role=tab] >> nth=0")).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(page.Locator("#heard [role=tab][data-selected]")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task A_match_find_in_page_is_about_to_reveal_selects_its_tab_in_the_browser()
    {
        await using var watched = await Watched.OpenAsync(playwright);
        var page = watched.Session.Page;

        await page.EvaluateAsync(
            "() => document.querySelector('#findable [role=tabpanel][hidden=until-found]').dispatchEvent(new Event('beforematch', {bubbles: true}))");

        await Expect(page.Locator("#findable [role=tab] >> nth=1")).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(page.Locator("#findable [role=tabpanel]:not([hidden])")).ToHaveTextAsync("Found here.");
        await Expect(page.Locator("#findable [role=tabpanel][hidden=until-found]")).ToHaveCountAsync(1);
        Assert.Equal((0, 0), watched.Frames());
    }

    [Fact]
    public async Task A_long_form_in_tabs_keeps_what_was_typed_on_every_tab_and_a_refused_save_opens_the_tab_with_the_mistake()
    {
        await using var watched = await Watched.OpenAsync(playwright);
        var page = watched.Session.Page;
        await page.FillAsync("#name", "Ada");
        await page.ClickAsync("#form [role=tab] >> nth=2");
        await page.FillAsync("#plan", "pro");
        var typing = watched.Frames();

        await page.ClickAsync("#save");

        // Nothing was sent while the reader typed on two tabs; the save found the middle one empty and opened it.
        Assert.Equal((0, 0), typing);
        await Expect(page.Locator("#saved")).ToHaveTextAsync("refused: email");
        await Expect(page.Locator("#form [role=tab] >> nth=1")).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(page.Locator("#email")).ToBeVisibleAsync();
        await Expect(page.Locator("#name")).ToHaveValueAsync("Ada");
        await Expect(page.Locator("#plan")).ToHaveValueAsync("pro");
        await page.FillAsync("#email", "ada@example.com");
        await page.ClickAsync("#save");
        await Expect(page.Locator("#saved")).ToHaveTextAsync("saved: Ada, ada@example.com, pro");
    }

    [Fact]
    public async Task A_refused_save_opens_the_tab_the_page_rendered_first_though_no_diff_says_so()
    {
        await using var watched = await Watched.OpenAsync(playwright);
        var page = watched.Session.Page;
        await page.ClickAsync("#form [role=tab] >> nth=2");
        await page.FillAsync("#plan", "pro");

        await page.ClickAsync("#save");

        await Expect(page.Locator("#saved")).ToHaveTextAsync("refused: name");
        await Expect(page.Locator("#form [role=tab] >> nth=0")).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(page.Locator("#name")).ToBeVisibleAsync();
        await Expect(page.Locator("#plan")).ToHaveValueAsync("pro");
    }

    [Fact]
    public async Task A_tab_chosen_in_a_form_that_asks_before_it_is_left_is_an_unsaved_change_as_any_bound_pick_is()
    {
        await using var watched = await Watched.OpenAsync(playwright);
        var page = watched.Session.Page;
        await page.ClickAsync("#guarded [role=tab] >> nth=1");
        var chosen = watched.Frames();

        await page.ClickAsync("#away");

        Assert.Equal((0, 0), chosen);
        await Expect(page.Locator("dialog[open] button").Filter(new() { HasTextString = "Stay" })).ToBeVisibleAsync();
        Assert.DoesNotContain("/elsewhere", page.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_form_whose_tabs_nobody_touched_is_left_without_a_question()
    {
        await using var watched = await Watched.OpenAsync(playwright);
        var page = watched.Session.Page;

        await page.ClickAsync("#away");

        await page.WaitForURLAsync("**/elsewhere");
        await Expect(page.Locator("dialog[open]")).ToHaveCountAsync(0);
    }

    // aria-selected of each tab, each tab's tabindex, and the tab that has focus.
    private const string Row = """
        id => {
            const tabs = [...document.querySelectorAll('#' + id + ' [role=tab]')];
            return tabs.map(t => t.getAttribute('aria-selected')).join(' ') + '|' + tabs.map(t => t.getAttribute('tabindex')).join(' ')
                + '|' + (document.activeElement.getAttribute('data-rask-tab') || '');
        }
        """;

    // A session whose socket is counted from the moment the page and its hooks are in.
    private sealed class Watched(HookSession session, int[] counts) : IAsyncDisposable
    {
        private readonly int _sentBefore = Volatile.Read(ref counts[0]);
        private readonly int _receivedBefore = Volatile.Read(ref counts[1]);

        public HookSession Session => session;

        public static async Task<Watched> OpenAsync(PlaywrightFixture playwright)
        {
            var counts = new int[2];
            var session = await HookSession.OpenAsync<TabsBrowserPage>(playwright, beforeLoad: page =>
            {
                page.WebSocket += (_, socket) =>
                {
                    socket.FrameSent += (_, _) => Interlocked.Increment(ref counts[0]);
                    socket.FrameReceived += (_, _) => Interlocked.Increment(ref counts[1]);
                };
                return Task.CompletedTask;
            });
            await session.HooksLoadedAsync();
            // Whatever the load itself exchanged has gone by: from here on the socket is watched.
            await session.Page.WaitForTimeoutAsync(300);
            return new Watched(session, counts);
        }

        /// <summary>Frames sent and received since the page settled.</summary>
        public (int Sent, int Received) Frames() =>
            (Volatile.Read(ref counts[0]) - _sentBefore, Volatile.Read(ref counts[1]) - _receivedBefore);

        public ValueTask DisposeAsync() => session.DisposeAsync();
    }
}

/// <summary>Tab rows held each way a page can hold one, and a form whose fields are spread over three tabs.</summary>
public sealed partial class TabsBrowserPage : Component
{
    private readonly View _view = new();
    private readonly Profile _profile = new();
    private readonly Draft _draft = new();
    private int _ticks;
    private int _refused;
    private string _heard = "list";
    private string _saved = "";

    protected override Component? HeadAssets => [Markup.Title["tabs in the browser"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
        Div.Style("width:480px;margin:16px")[
            Button.Id("tick").OnClick(() => _ticks++)[$"Ticked {_ticks}"],
            Div.Id("own")[Ui.TabGroup.Key("own")[
                Ui.Tabs[Ui.Tab.Name("profile")["Profile"], Ui.Tab.Name("account")["Account"], Ui.Tab.Name("billing")["Billing"]],
                Ui.TabPanel.Name("profile")["Your name."],
                Ui.TabPanel.Name("account")["Your email."],
                Ui.TabPanel.Name("billing")["Your plan."]
            ]],
            Div.Id("bound")[Views(Ui.Tabs.Key("bound").Segmented.Bind(() => _view.Deferred))],
            P.Id("bound-says")[_view.Deferred],
            Div.Id("live")[Views(Ui.Tabs.Key("live").Segmented.Bind(() => _view.Live).Live())],
            P.Id("live-says")[_view.Live],
            Div.Id("heard")[Views(Ui.Tabs.Key("heard").Segmented.Value(_heard).OnChange(Heard))],
            P.Id("heard-says")[_refused == 0 ? _heard : $"{_heard}, refused {_refused}"],
            Div.Id("findable")[Ui.TabGroup.Key("findable").Findable()[
                Ui.Tabs[Ui.Tab.Name("shown")["Shown"], Ui.Tab.Name("found")["Found"]],
                Ui.TabPanel.Name("shown")["Shown here."],
                Ui.TabPanel.Name("found")["Found here."]
            ]],
            P.Id("saved")[_saved],
            Div.Id("form")[Form.Model(_profile).OnSubmit(Save)[
                Ui.TabGroup.Key("form")[
                    Ui.Tabs.Bind(() => _view.Form)[Ui.Tab.Name("name")["Name"], Ui.Tab.Name("email")["Email"], Ui.Tab.Name("plan")["Plan"]],
                    Ui.TabPanel.Name("name")[Ui.Input.Bind(() => _profile.Name).Label("Name").Id("name")],
                    Ui.TabPanel.Name("email")[Ui.Input.Bind(() => _profile.Email).Label("Email").Id("email")],
                    Ui.TabPanel.Name("plan")[Ui.Input.Bind(() => _profile.Plan).Label("Plan").Id("plan")]
                ],
                Ui.Button.Submit.Id("save")["Save"]
            ]],
            Div.Id("guarded")[Form.Model(_draft).OnSubmit(_ => { }).NoValidate(true).ConfirmLeave("Leave without saving?")[
                Views(Ui.Tabs.Key("guarded").Segmented.Bind(() => _draft.View))
            ]],
            NavLink.Href("/elsewhere").Id("away")["Elsewhere"],
            Ui.ConfirmLeave.Stay("Stay").Leave("Leave")
        ];

    private static Component Views(UiTabs row) =>
        row[Ui.Tab.Name("list")["List"], Ui.Tab.Name("board")["Board"], Ui.Tab.Name("timeline")["Timeline"]];

    // The page's own rule: the timeline is not to be had.
    private void Heard(string view)
    {
        if (string.Equals(view, "timeline", StringComparison.Ordinal))
        {
            _refused++;
            return;
        }

        _heard = view;
    }

    private void Save(Profile profile)
    {
        var missing = string.IsNullOrEmpty(profile.Name) ? "name" : string.IsNullOrEmpty(profile.Email) ? "email" : null;
        if (missing is null)
        {
            _saved = $"saved: {profile.Name}, {profile.Email}, {profile.Plan}";
            return;
        }

        _view.Form = missing;
        _saved = "refused: " + missing;
    }

    private sealed class View
    {
        public string Deferred { get; set; } = "list";

        public string Live { get; set; } = "list";

        public string Form { get; set; } = "name";
    }

    private sealed class Draft
    {
        public string View { get; set; } = "list";
    }

    private sealed class Profile
    {
        public string Name { get; set; } = "";

        public string Email { get; set; } = "";

        public string Plan { get; set; } = "";
    }
}
