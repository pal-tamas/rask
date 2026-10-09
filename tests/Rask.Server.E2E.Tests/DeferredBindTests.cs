using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Forms;
using Rask.Core.Live;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // small test pages; their <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A bound control with no timing step, in a real browser over the real socket: nothing is sent while the
///     reader types, chooses or leaves, and everything they entered goes ahead of the next action.
/// </summary>
/// <remarks>
///     Every journey runs against a form of plain Core controls and against the kit's, which only forward to
///     them. "Nothing was sent" is counted on the socket itself. The session fails any journey whose page
///     raised an error nothing caught.
/// </remarks>
public sealed class DeferredBindTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    // Longer than Livewire's 150 ms and any debounce the page counts.
    private const int Wait = 400;

    private Task<HookSession> OpenAsync(bool kit, Func<IPage, Task>? beforeLoad = null, Action<RaskLiveOptions>? live = null) =>
        kit
            ? HookSession.OpenAsync<DeferredKitPage>(playwright, beforeLoad: beforeLoad, live: live)
            : HookSession.OpenAsync<DeferredCorePage>(playwright, beforeLoad: beforeLoad, live: live);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Typing_into_three_fields_and_leaving_them_sends_nothing_and_Save_carries_all_three(bool kit)
    {
        await using var session = await OpenAsync(kit, CountFrames);
        var page = session.Page;
        await Ready(page);

        var before = await SentAsync(page);
        var framesBefore = await FramesAsync(page);
        await page.Locator("#name").PressSequentiallyAsync("Ada");
        await page.Locator("#notes").PressSequentiallyAsync("Two lines");
        await page.Locator("#age").PressSequentiallyAsync("36");
        await page.Locator("#age").PressAsync("Tab");
        await page.WaitForTimeoutAsync(Wait);
        var whileTyping = await SentAsync(page) - before;
        var modelWhileTyping = await page.Locator("#model").TextContentAsync();
        await page.ClickAsync("#save");

        await Expect(page.Locator("#saved")).ToHaveTextAsync("saved=Ada|Two lines|36");
        Assert.Equal(0, whileTyping);
        Assert.Equal("name=|notes=|age=", modelWhileTyping);
        // One change a field, in the order they were typed into, then the submit: four things said, in one frame.
        Assert.Equal(4, await SentAsync(page) - before);
        Assert.Equal(1, await FramesAsync(page) - framesBefore);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Enter_in_a_field_submits_what_was_typed_into_it(bool kit)
    {
        await using var session = await OpenAsync(kit);
        var page = session.Page;
        await Ready(page);

        await page.Locator("#notes").PressSequentiallyAsync("Kept");
        await page.Locator("#name").PressSequentiallyAsync("Grace");
        await page.Locator("#name").PressAsync("Enter");

        await Expect(page.Locator("#saved")).ToHaveTextAsync("saved=Grace|Kept|");
    }

    // A render the reader did not ask for — a handler that finishes later, a push from the server. Nothing
    // flushed the fields first, and every render is sent whole here, so each one is a morph over fields that
    // hold text the server has never heard, none of them focused by then.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_render_from_the_server_leaves_what_was_typed_and_left_and_Save_still_carries_it(bool kit)
    {
        await using var session = await OpenAsync(kit, live: o => o.DiffMode = LiveDiffMode.DisabledFull);
        var page = session.Page;
        await Ready(page);

        await page.ClickAsync("#later");
        await page.Locator("#name").PressSequentiallyAsync("Linus");
        await page.Locator("#notes").PressSequentiallyAsync("Unsent");
        await page.Locator("#age").PressSequentiallyAsync("55");
        await page.Locator("#count").FocusAsync();
        await Expect(page.Locator("#ticked")).ToHaveTextAsync("ticks=1");
        var modelAfterThePush = await page.Locator("#model").TextContentAsync();

        await Expect(page.Locator("#name")).ToHaveValueAsync("Linus");
        await Expect(page.Locator("#notes")).ToHaveValueAsync("Unsent");
        await Expect(page.Locator("#age")).ToHaveValueAsync("55");
        Assert.Equal("name=|notes=|age=", modelAfterThePush);
        await page.ClickAsync("#save");
        await Expect(page.Locator("#saved")).ToHaveTextAsync("saved=Linus|Unsent|55");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_refused_submit_shows_its_message_which_goes_at_the_first_key_of_the_correction(bool kit)
    {
        await using var session = await OpenAsync(kit, CountFrames);
        var page = session.Page;
        await Ready(page);
        await page.Locator("#name").PressSequentiallyAsync("Al");
        var messagesWhileTyping = await page.Locator("#name-error").CountAsync();
        await page.Locator("#name").PressAsync("Enter");
        await Expect(page.Locator("#name-error")).ToHaveTextAsync("Name is too short.");

        var before = await SentAsync(page);
        await page.Locator("#name").PressSequentiallyAsync("a");
        await Expect(page.Locator("#name-error")).ToHaveCountAsync(0);
        await page.Locator("#name").PressSequentiallyAsync("n", new() { Delay = 30 });
        await page.WaitForTimeoutAsync(Wait);
        var sentByTheCorrection = await SentAsync(page) - before;

        Assert.Equal(0, messagesWhileTyping);
        // The first key said the message was stale. No value went with it, and none after it.
        Assert.Equal(1, sentByTheCorrection);
        await Expect(page.Locator("#saved")).ToHaveTextAsync("saved=");
        await page.Locator("#name").PressAsync("Enter");
        await Expect(page.Locator("#saved")).ToHaveTextAsync("saved=Alan||");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_live_field_is_read_back_once_typing_has_paused(bool kit)
    {
        await using var session = await OpenAsync(kit, CountFrames);
        var page = session.Page;
        await Ready(page);

        var before = await SentAsync(page);
        await page.Locator("#title").PressSequentiallyAsync("Hello", new() { Delay = 20 });
        await Expect(page.Locator("#title-out")).ToHaveTextAsync("title=Hello");
        await page.WaitForTimeoutAsync(Wait);

        Assert.Equal(1, await SentAsync(page) - before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_live_field_carries_what_was_typed_into_the_fields_before_it(bool kit)
    {
        await using var session = await OpenAsync(kit);
        var page = session.Page;
        await Ready(page);

        await page.Locator("#name").PressSequentiallyAsync("Margaret");
        await page.Locator("#title").PressSequentiallyAsync("Dr");

        await Expect(page.Locator("#title-out")).ToHaveTextAsync("title=Dr");
        await Expect(page.Locator("#model")).ToHaveTextAsync("name=Margaret|notes=|age=");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_first_keystroke_in_a_field_is_an_unsaved_change_to_the_leave_guard_and_sends_nothing(bool kit)
    {
        await using var session = await OpenAsync(kit, CountFrames);
        var page = session.Page;
        await Ready(page);
        await session.HooksLoadedAsync();
        var cleanBefore = !await UnsavedAsync(page);

        var before = await SentAsync(page);
        await page.Locator("#name").PressSequentiallyAsync("P");
        var unsaved = await UnsavedAsync(page);
        var sent = await SentAsync(page) - before;

        Assert.True(cleanBefore);
        Assert.True(unsaved);
        Assert.Equal(0, sent);
    }

    [Fact]
    public async Task A_checkbox_a_select_and_a_date_say_nothing_when_chosen_and_travel_with_Save()
    {
        await using var session = await OpenAsync(kit: false, CountFrames);
        var page = session.Page;
        await Ready(page);

        var before = await SentAsync(page);
        await page.Locator("#agree").CheckAsync();
        await page.Locator("#plan").SelectOptionAsync("pro");
        await page.Locator("#day").FillAsync("2026-10-09");
        await page.Locator("#day").PressAsync("Tab");
        await page.WaitForTimeoutAsync(Wait);
        var whileChoosing = await SentAsync(page) - before;
        var choicesBeforeSave = await page.Locator("#choices").TextContentAsync();
        await page.ClickAsync("#save");

        await Expect(page.Locator("#choices")).ToHaveTextAsync("agree=True|plan=pro|day=2026-10-09");
        Assert.Equal(0, whileChoosing);
        Assert.Equal("agree=False|plan=free|day=", choicesBeforeSave);
    }

    [Fact]
    public async Task A_render_from_the_server_leaves_a_tick_a_pick_and_a_date_the_reader_made()
    {
        await using var session = await OpenAsync(kit: false, live: o => o.DiffMode = LiveDiffMode.DisabledFull);
        var page = session.Page;
        await Ready(page);

        await page.ClickAsync("#later");
        await page.Locator("#agree").CheckAsync();
        await page.Locator("#plan").SelectOptionAsync("pro");
        await page.Locator("#day").FillAsync("2026-10-09");
        await page.Locator("#count").FocusAsync();
        await Expect(page.Locator("#ticked")).ToHaveTextAsync("ticks=1");
        var choicesAfterThePush = await page.Locator("#choices").TextContentAsync();

        await Expect(page.Locator("#agree")).ToBeCheckedAsync();
        await Expect(page.Locator("#plan")).ToHaveValueAsync("pro");
        await Expect(page.Locator("#day")).ToHaveValueAsync("2026-10-09");
        Assert.Equal("agree=False|plan=free|day=", choicesAfterThePush);
        await page.ClickAsync("#save");
        await Expect(page.Locator("#choices")).ToHaveTextAsync("agree=True|plan=pro|day=2026-10-09");
    }

    [Fact]
    public async Task A_live_select_is_one_round_trip_a_pick_and_carries_the_text_typed_before_it()
    {
        await using var session = await OpenAsync(kit: false, CountFrames);
        var page = session.Page;
        await Ready(page);
        await page.Locator("#name").PressSequentiallyAsync("Barbara");

        var before = await SentAsync(page);
        await page.Locator("#topic").SelectOptionAsync("billing");
        await Expect(page.Locator("#heard")).ToHaveTextAsync("heard=billing by Barbara");
        var forThePickAndTheName = await SentAsync(page) - before;
        await page.Locator("#topic").SelectOptionAsync("sales");
        await Expect(page.Locator("#heard")).ToHaveTextAsync("heard=sales by Barbara");

        // The name that waited, then the pick. The second pick is alone.
        Assert.Equal(2, forThePickAndTheName);
        Assert.Equal(3, await SentAsync(page) - before);
    }

    // The form two apps downstream lost values in: a text field, a multiple listbox over a list, two numbers,
    // a native select and a checkbox, filled in without a pause and saved. Every reply is the whole page here,
    // the worst case: each pick in the listbox, each tick and each choice is answered with one, over fields
    // that hold text the server has not heard — left behind, or still being typed into.
    [Fact]
    public async Task A_form_filled_in_without_a_pause_reaches_the_handler_whole_though_every_reply_is_a_whole_page()
    {
        await using var session = await HookSession.OpenAsync<DeferredRangeFormPage>(
            playwright, live: o => o.DiffMode = LiveDiffMode.DisabledFull);
        var page = session.Page;
        await Ready(page);

        await page.GetByLabel("Name").FillAsync("Range one");
        await page.ClickAsync("[data-ui-select-button]");
        await Expect(page.Locator("[data-ui-select-button]")).ToHaveAttributeAsync("aria-expanded", "true");
        await page.Locator("[data-ui-option]").Filter(new() { HasText = "Glock" }).ClickAsync();
        await page.Locator("[data-ui-option]").Filter(new() { HasText = "Beretta" }).ClickAsync();
        await page.Keyboard.PressAsync("Escape");
        await page.GetByLabel("Lanes").FillAsync("12");
        await page.GetByLabel("Size").FillAsync("600");
        await page.GetByLabel("Kind").SelectOptionAsync("outdoor");
        await page.GetByLabel("Open to visitors").CheckAsync();
        await page.ClickAsync("#save");

        await Expect(page.Locator("#saved")).ToHaveTextAsync("saved=Range one|Glock,Beretta|12|600|outdoor|True");
    }

    [Fact]
    public async Task A_whole_page_reply_that_lands_while_a_number_is_being_typed_leaves_it_and_the_fields_already_left()
    {
        await using var session = await HookSession.OpenAsync<DeferredRangeFormPage>(
            playwright, live: o => o.DiffMode = LiveDiffMode.DisabledFull);
        var page = session.Page;
        await Ready(page);

        await page.ClickAsync("#later");
        await page.GetByLabel("Name").FillAsync("Range two");
        await page.GetByLabel("Lanes").FillAsync("8");
        await page.GetByLabel("Size").PressSequentiallyAsync("45");
        await Expect(page.Locator("#ticked")).ToHaveTextAsync("ticks=1");
        var focused = await page.EvaluateAsync<string>("() => document.activeElement.getAttribute('name')");
        await page.GetByLabel("Size").PressSequentiallyAsync("0");

        Assert.Equal("Size", focused);
        await Expect(page.GetByLabel("Name")).ToHaveValueAsync("Range two");
        await Expect(page.GetByLabel("Lanes")).ToHaveValueAsync("8");
        await Expect(page.GetByLabel("Size")).ToHaveValueAsync("450");
        await page.ClickAsync("#save");
        await Expect(page.Locator("#saved")).ToHaveTextAsync("saved=Range two||8|450|indoor|False");
    }

    // What the page said over the socket, counted in the page: Playwright's own frame events arrive on its own
    // schedule, and a count read "now" must be the count now. The events of one task leave as one batch
    // (rask-batch.ts), so both are kept: the things said, and the frames they left in.
    private static Task CountFrames(IPage page) => page.AddInitScriptAsync(
        """
        window.__sent = 0;
        window.__frames = 0;
        const send = WebSocket.prototype.send;
        WebSocket.prototype.send = function (data) {
            const frame = JSON.parse(data);
            window.__sent += frame.type === "batch" ? frame.events.length : 1;
            window.__frames++;
            return send.call(this, data);
        };
        """);

    private static Task<int> SentAsync(IPage page) => page.EvaluateAsync<int>("() => window.__sent");

    private static Task<int> FramesAsync(IPage page) => page.EvaluateAsync<int>("() => window.__frames");

    // The page is live once a press reaches the server and comes back.
    private static async Task Ready(IPage page)
    {
        await page.ClickAsync("#ready");
        await Expect(page.Locator("#is-ready")).ToHaveTextAsync("ready");
    }

    private static Task<bool> UnsavedAsync(IPage page) =>
        page.EvaluateAsync<bool>("() => { const ask = window.confirm; let asked = false; window.confirm = () => { asked = true; return true; }; window.__raskHookSeam.leave(); window.confirm = ask; return asked; }");
}

internal sealed class Profile
{
    public string Name { get; set; } = "";

    public string Notes { get; set; } = "";

    public int? Age { get; set; }

    public string Title { get; set; } = "";

    public bool Agreed { get; set; }

    public string Plan { get; set; } = "free";

    public DateOnly? Day { get; set; }

    public string Topic { get; set; } = "";

    public static Validate<string> TooShort { get; } =
        value => value.Length is > 0 and < 3 ? ["Name is too short."] : [];

    public string Typed => $"name={Name}|notes={Notes}|age={Age}";

    public string Chosen => $"agree={Agreed}|plan={Plan}|day={Day:yyyy-MM-dd}";
}

/// <summary>A form of plain Core controls with no timing step, one live field, one live select, and a render that comes later.</summary>
public sealed partial class DeferredCorePage : Component
{
    private readonly Profile _profile = new();
    private string _saved = "";
    private string _heard = "";
    private int _ticks;
    private int _count;
    private bool _ready;

    protected override Component? HeadAssets => Markup.Title["deferred bind, core"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("model")[_profile.Typed],
        P.Id("choices")[_profile.Chosen],
        P.Id("title-out")[$"title={_profile.Title}"],
        P.Id("heard")[$"heard={_heard}"],
        P.Id("saved")[$"saved={_saved}"],
        P.Id("ticked")[$"ticks={_ticks}"],
        P.Id("counted")[$"count={_count}"],
        P.Id("is-ready")[_ready ? "ready" : "loading"],
        Button.Id("ready").OnClick(() => _ready = true)["ready"],
        Button.Id("later").OnClick(TickLater)["later"],
        Button.Id("count").OnClick(() => _count++)["count"],
        Form.Model(_profile).OnSubmit(Save).NoValidate(true).ConfirmLeave("Leave without saving?")[
            Input.Bind(() => _profile.Name).Id("name").Validate(Profile.TooShort),
            Validation.Message.Template(messages => P.Id("name-error")[messages[0]]).For(() => _profile.Name),
            Textarea.Bind(() => _profile.Notes).Id("notes"),
            Input.Bind(() => _profile.Age).Id("age"),
            Input.Bind(() => _profile.Title).Id("title").Live(),
            Input.Bind(() => _profile.Agreed).Id("agree"),
            Select.Bind(() => _profile.Plan).Id("plan")[Option.Value("free")["Free"], Option.Value("pro")["Pro"]],
            Input.Bind(() => _profile.Day).Id("day"),
            Select.Bind(() => _profile.Topic).Id("topic").Live().AfterBind(topic => { _heard = $"{topic} by {_profile.Name}"; })[
                Option.Value("")["—"], Option.Value("billing")["Billing"], Option.Value("sales")["Sales"]
            ],
            Button.Type(ButtonType.Submit).Id("save")["save"]
        ]
    ];

    // Renders after the reader has gone on to type: a render nothing they did just now asked for.
    private async Task TickLater()
    {
        await Task.Delay(300);
        _ticks++;
    }

    private void Save(Profile profile) => _saved = $"{profile.Name}|{profile.Notes}|{profile.Age}";
}

/// <summary>The same form drawn with the kit's controls, which forward every timing step to Core's.</summary>
public sealed partial class DeferredKitPage : Component
{
    private readonly Profile _profile = new();
    private string _saved = "";
    private int _ticks;
    private int _count;
    private bool _ready;

    protected override Component? HeadAssets => Markup.Title["deferred bind, kit"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("model")[_profile.Typed],
        P.Id("title-out")[$"title={_profile.Title}"],
        P.Id("saved")[$"saved={_saved}"],
        P.Id("ticked")[$"ticks={_ticks}"],
        P.Id("counted")[$"count={_count}"],
        P.Id("is-ready")[_ready ? "ready" : "loading"],
        Button.Id("ready").OnClick(() => _ready = true)["ready"],
        Button.Id("later").OnClick(TickLater)["later"],
        Button.Id("count").OnClick(() => _count++)["count"],
        Form.Model(_profile).OnSubmit(Save).NoValidate(true).ConfirmLeave("Leave without saving?")[
            Ui.Input.Bind(() => _profile.Name).Id("name").Validate(Profile.TooShort).ShowValidation(false),
            Validation.Message.Template(messages => P.Id("name-error")[messages[0]]).For(() => _profile.Name),
            Ui.Textarea.Bind(() => _profile.Notes).Id("notes"),
            Ui.Input.Bind(() => _profile.Age).Id("age"),
            Ui.Input.Bind(() => _profile.Title).Id("title").Live(),
            Ui.Button.Submit.Id("save")["save"]
        ]
    ];

    private async Task TickLater()
    {
        await Task.Delay(300);
        _ticks++;
    }

    private void Save(Profile profile) => _saved = $"{profile.Name}|{profile.Notes}|{profile.Age}";
}

/// <summary>
///     The form reported from downstream, drawn with the kit: text, a multiple listbox over a list, two numbers,
///     a native select, a checkbox, and a render that comes later.
/// </summary>
public sealed partial class DeferredRangeFormPage : Component
{
    private static readonly string[] Pistols =
    [
        "Glock", "Beretta", "Walther", "Sig Sauer", "Colt", "Ruger", "Smith & Wesson", "Heckler & Koch", "CZ",
        "Springfield", "Taurus", "Kimber", "Steyr",
    ];

    private readonly Range _form = new();
    private string _saved = "";
    private int _ticks;
    private bool _ready;

    protected override Component? HeadAssets => Markup.Title["deferred bind, the reported form"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("saved")[$"saved={_saved}"],
        P.Id("ticked")[$"ticks={_ticks}"],
        P.Id("is-ready")[_ready ? "ready" : "loading"],
        Button.Id("ready").OnClick(() => _ready = true)["ready"],
        Button.Id("later").OnClick(TickLater)["later"],
        Form.Model(_form).OnSubmit(Save).ConfirmLeave("Leave without saving?")[
            Ui.Field[Ui.Label["Name"], Ui.Input.Bind(() => _form.Name)],
            Ui.Field[
                Ui.Label["Pistols"],
                Ui.Select.Bind(() => _form.Ids).Listbox.Multiple().Placeholder("Choose…")[
                    Pistols.Select(pistol => Ui.SelectOption.Key(pistol).Value(pistol)[pistol])
                ]
            ],
            Ui.Field[Ui.Label["Lanes"], Ui.Input.Bind(() => _form.Lanes).Type(InputType.Number)],
            Ui.Field[Ui.Label["Size"], Ui.Input.Bind(() => _form.Size).Type(InputType.Number)],
            Ui.Field[
                Ui.Label["Kind"],
                Ui.Select.Bind(() => _form.Kind)[
                    Ui.SelectOption.Value("indoor")["Indoor"], Ui.SelectOption.Value("outdoor")["Outdoor"]
                ]
            ],
            Ui.Checkbox.Bind(() => _form.Open).Label("Open to visitors"),
            Ui.Button.Primary.Submit.Id("save")["Save"]
        ]
    ];

    private async Task TickLater()
    {
        await Task.Delay(400);
        _ticks++;
    }

    private void Save() =>
        _saved = $"{_form.Name}|{string.Join(',', _form.Ids)}|{_form.Lanes}|{_form.Size}|{_form.Kind}|{_form.Open}";

    private sealed class Range
    {
        public string Name { get; set; } = "";

        public List<string> Ids { get; set; } = [];

        public int? Lanes { get; set; }

        public int? Size { get; set; }

        public string Kind { get; set; } = "indoor";

        public bool Open { get; set; }
    }
}
