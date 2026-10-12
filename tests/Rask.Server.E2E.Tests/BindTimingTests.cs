using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Forms;
using Rask.Core.Live;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     <c>.Debounce(…)</c> and <c>.Blur()</c> on a bound field, in a real browser over the real socket: what is
///     sent while the reader types, at the pause, at Enter and on the way out of the field.
/// </summary>
/// <remarks>
///     "Nothing was sent" is counted on the socket itself, so a field that streamed after all cannot pass by
///     rendering the same thing. The session fails any journey whose page raised an error nothing caught.
/// </remarks>
public sealed class BindTimingTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    // Longer than any keystroke gap a loaded runner produces, short enough to wait for.
    private const int Pause = BindTimingPage.PauseMilliseconds;

    [Fact]
    public async Task Typing_into_a_debounced_field_sends_nothing_until_the_pause_and_then_one_message()
    {
        await using var session = await HookSession.OpenAsync<BindTimingPage>(playwright, beforeLoad: CountFrames);
        var page = session.Page;
        await Ready(page);

        var before = await SentAsync(page);
        await page.Locator("#name").PressSequentiallyAsync("Atlan", new() { Delay = 30 });
        var whileTyping = await SentAsync(page) - before;
        await Expect(page.Locator("#model")).ToHaveTextAsync("name=Atlan|city=");
        await page.WaitForTimeoutAsync(Pause);
        var afterPause = await SentAsync(page) - before;

        Assert.Equal(0, whileTyping);
        Assert.Equal(1, afterPause);
    }

    [Fact]
    public async Task Both_rules_of_a_debounced_field_answer_in_the_one_round_trip_the_pause_makes()
    {
        await using var session = await HookSession.OpenAsync<BindTimingPage>(playwright, beforeLoad: CountFrames);
        var page = session.Page;
        await Ready(page);

        var before = await SentAsync(page);
        await page.Locator("#name").PressSequentiallyAsync("At");
        await Expect(page.Locator("#name-error")).ToHaveTextAsync("Name is too short.");
        var lookupsWhileShort = await page.Locator("#lookups").TextContentAsync();
        var sentForShort = await SentAsync(page) - before;
        await page.Locator("#name").PressSequentiallyAsync("lantis");
        await Expect(page.Locator("#name-error")).ToHaveTextAsync("Name is taken.");

        Assert.Equal("lookups=0", lookupsWhileShort);
        Assert.Equal(1, sentForShort);
        await Expect(page.Locator("#lookups")).ToHaveTextAsync("lookups=1");
    }

    [Fact]
    public async Task A_message_under_a_debounced_field_goes_at_the_first_keystroke_not_at_the_next_pause()
    {
        await using var session = await HookSession.OpenAsync<BindTimingPage>(playwright, beforeLoad: CountFrames);
        var page = session.Page;
        await Ready(page);
        await page.Locator("#name").PressSequentiallyAsync("At");
        await Expect(page.Locator("#name-error")).ToHaveTextAsync("Name is too short.");

        var before = await SentAsync(page);
        await page.Locator("#name").PressSequentiallyAsync("l");
        await Expect(page.Locator("#name-error")).ToHaveCountAsync(0, new() { Timeout = Pause / 2 });
        var sentAtTheKey = await SentAsync(page) - before;
        await page.Locator("#name").PressSequentiallyAsync("an", new() { Delay = 30 });
        var sentByMoreKeys = await SentAsync(page) - before;

        Assert.Equal(1, sentAtTheKey);
        Assert.Equal(1, sentByMoreKeys);
        await Expect(page.Locator("#model")).ToHaveTextAsync("name=Atlan|city=");
    }

    [Fact]
    public async Task Enter_in_a_debounced_field_sends_what_was_typed_ahead_of_the_submit()
    {
        await using var session = await HookSession.OpenAsync<BindTimingPage>(playwright);
        var page = session.Page;
        await Ready(page);

        await page.Locator("#name").PressSequentiallyAsync("Paris");
        await page.Locator("#name").PressAsync("Enter");

        await Expect(page.Locator("#saved")).ToHaveTextAsync("saved=Paris");
    }

    [Fact]
    public async Task Enter_on_a_value_the_rule_rejects_saves_nothing_and_shows_why()
    {
        await using var session = await HookSession.OpenAsync<BindTimingPage>(playwright);
        var page = session.Page;
        await Ready(page);

        await page.Locator("#name").PressSequentiallyAsync("Atlantis");
        await page.Locator("#name").PressAsync("Enter");

        await Expect(page.Locator("#name-error")).ToHaveTextAsync("Name is taken.");
        await Expect(page.Locator("#saved")).ToHaveTextAsync("saved=");
    }

    [Fact]
    public async Task A_field_bound_on_blur_sends_nothing_while_typed_into_and_its_message_shows_on_leaving()
    {
        await using var session = await HookSession.OpenAsync<BindTimingPage>(playwright, beforeLoad: CountFrames);
        var page = session.Page;
        await Ready(page);

        var before = await SentAsync(page);
        await page.Locator("#city").PressSequentiallyAsync("Ro");
        await page.WaitForTimeoutAsync(Pause);
        var whileTyping = await SentAsync(page) - before;
        await page.Locator("#city").PressAsync("Tab");
        await Expect(page.Locator("#city-error")).ToHaveTextAsync("City is too short.");
        var afterLeaving = await SentAsync(page) - before;

        Assert.Equal(0, whileTyping);
        Assert.Equal(1, afterLeaving);
        await Expect(page.Locator("#model")).ToHaveTextAsync("name=|city=Ro");
    }

    [Fact]
    public async Task Enter_in_a_field_bound_on_blur_saves_what_was_typed_and_sends_its_change_once()
    {
        await using var session = await HookSession.OpenAsync<BindTimingPage>(playwright, beforeLoad: CountFrames);
        var page = session.Page;
        await Ready(page);
        await page.Locator("#name").FillAsync("Paris");
        await Expect(page.Locator("#model")).ToHaveTextAsync("name=Paris|city=");

        var before = await SentAsync(page);
        await page.Locator("#city").PressSequentiallyAsync("Lyon");
        await page.Locator("#city").PressAsync("Enter");
        await Expect(page.Locator("#saved")).ToHaveTextAsync("saved=Paris/Lyon");
        await page.Locator("#city").PressAsync("Tab");
        await page.ClickAsync("#count");
        await Expect(page.Locator("#counted")).ToHaveTextAsync("count=1");

        // The change, the submit, the click: leaving the field afterwards said nothing a second time.
        Assert.Equal(3, await SentAsync(page) - before);
    }

    // A handled key sends the field ahead of itself, at the keydown. Leaving the field afterwards makes the
    // browser fire `change` for a value the page already has.
    [Fact]
    public async Task A_change_fired_for_a_value_a_handled_key_already_sent_is_not_sent_again()
    {
        await using var session = await HookSession.OpenAsync<BindTimingPage>(playwright, beforeLoad: CountFrames);
        var page = session.Page;
        await Ready(page);

        var before = await SentAsync(page);
        await page.Locator("#code").PressSequentiallyAsync("LYS");
        await page.Locator("#code").PressAsync("F2");
        await Expect(page.Locator("#code-model")).ToHaveTextAsync("code=LYS|keys=1");
        await page.Locator("#code").PressAsync("Tab");
        await page.ClickAsync("#count");
        await Expect(page.Locator("#counted")).ToHaveTextAsync("count=1");

        // The change the key sent ahead of itself, the key, the click.
        Assert.Equal(3, await SentAsync(page) - before);
    }

    // A render the reader did not ask for at that moment — a handler that finishes later, a push from the
    // server: nothing flushed the field first, as a click would have. Every render is sent whole here, so each
    // one is a morph: the path that let the rendered value win on a field with no input handler, focused or not.
    [Fact]
    public async Task A_render_that_lands_while_a_blur_bound_field_is_typed_into_leaves_what_was_typed()
    {
        await using var session = await HookSession.OpenAsync<BindTimingPage>(
            playwright, live: o => o.DiffMode = LiveDiffMode.DisabledFull);
        var page = session.Page;
        await Ready(page);

        await page.ClickAsync("#later");
        await page.Locator("#city").PressSequentiallyAsync("Marse");
        await Expect(page.Locator("#ticked")).ToHaveTextAsync("ticks=1");
        await page.Locator("#city").PressSequentiallyAsync("ille");
        await page.Locator("#city").PressAsync("Tab");

        await Expect(page.Locator("#city")).ToHaveValueAsync("Marseille");
        await Expect(page.Locator("#model")).ToHaveTextAsync("name=|city=Marseille");
    }

    [Fact]
    public async Task A_render_that_lands_between_a_keystroke_and_its_pause_leaves_what_was_typed()
    {
        await using var session = await HookSession.OpenAsync<BindTimingPage>(
            playwright, live: o => o.DiffMode = LiveDiffMode.DisabledFull);
        var page = session.Page;
        await Ready(page);

        await page.ClickAsync("#later");
        await page.Locator("#name").PressSequentiallyAsync("Par");
        await Expect(page.Locator("#ticked")).ToHaveTextAsync("ticks=1");
        await page.Locator("#name").PressSequentiallyAsync("is");

        await Expect(page.Locator("#model")).ToHaveTextAsync("name=Paris|city=");
        await Expect(page.Locator("#name")).ToHaveValueAsync("Paris");
    }

    [Theory]
    [InlineData("#name")]
    [InlineData("#city")]
    public async Task The_first_keystroke_in_a_waiting_field_is_an_unsaved_change_to_the_leave_guard(string field)
    {
        await using var session = await HookSession.OpenAsync<BindTimingPage>(playwright, beforeLoad: CountFrames);
        var page = session.Page;
        await Ready(page);
        await session.HooksLoadedAsync();
        var cleanBefore = !await UnsavedAsync(page);

        var before = await SentAsync(page);
        await page.Locator(field).PressSequentiallyAsync("P");
        var unsaved = await UnsavedAsync(page);
        var sent = await SentAsync(page) - before;

        Assert.True(cleanBefore);
        Assert.True(unsaved);
        Assert.Equal(0, sent);
    }

    // The socket's outgoing frames, counted in the page: Playwright's own frame events arrive on its own
    // schedule, and a count read "now" must be the count now.
    private static Task CountFrames(IPage page) => page.AddInitScriptAsync(
        """
        window.__sent = 0;
        const send = WebSocket.prototype.send;
        // What the page SAID, not how it was framed: the events of one task leave as one batch (rask-batch.ts),
        // so the value a key sends ahead of itself and the key are one frame, and two things said.
        WebSocket.prototype.send = function (data) {
            const frame = JSON.parse(data);
            window.__sent += frame.type === "batch" ? frame.events.length : 1;
            return send.call(this, data);
        };
        """);

    private static Task<int> SentAsync(IPage page) => page.EvaluateAsync<int>("() => window.__sent");

    // The page is live once a press reaches the server and comes back.
    private static async Task Ready(IPage page)
    {
        await page.ClickAsync("#ready");
        await Expect(page.Locator("#is-ready")).ToHaveTextAsync("ready");
    }

    private static Task<bool> UnsavedAsync(IPage page) =>
        page.EvaluateAsync<bool>("() => { const ask = window.confirm; let asked = false; window.confirm = () => { asked = true; return true; }; window.__raskHookSeam.leave(); window.confirm = ask; return asked; }");
}

internal sealed class Trip
{
    public string Name { get; set; } = "";

    public string City { get; set; } = "";

    public string Code { get; set; } = "";
}

/// <summary>A debounced field with a plain rule and a lookup, a field bound on blur, and a button whose render comes later.</summary>
public sealed partial class BindTimingPage : Component
{
    internal const int PauseMilliseconds = 400;

    private readonly Trip _trip = new();
    private string _saved = "";
    private int _keys;
    private int _lookups;
    private int _ticks;
    private int _count;
    private bool _ready;

    protected override Component? HeadAssets => Markup.Title["bind timing"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("model")[$"name={_trip.Name}|city={_trip.City}"],
        P.Id("code-model")[$"code={_trip.Code}|keys={_keys}"],
        P.Id("saved")[$"saved={_saved}"],
        P.Id("lookups")[$"lookups={_lookups}"],
        P.Id("ticked")[$"ticks={_ticks}"],
        P.Id("counted")[$"count={_count}"],
        P.Id("is-ready")[_ready ? "ready" : "loading"],
        Button.Id("ready").OnClick(() => _ready = true)["ready"],
        Button.Id("later").OnClick(TickLater)["later"],
        Button.Id("count").OnClick(() => _count++)["count"],
        Form.Model(_trip).OnSubmit(Save).NoValidate(true).ConfirmLeave("Leave without saving?")[
            Input.Bind(() => _trip.Name).Id("name")
                .Debounce(PauseMilliseconds.Milliseconds)
                .Validate(TooShort("Name"))
                .Validate(NameIsFree),
            Validation.Message.Template(messages => P.Id("name-error")[messages[0]]).For(() => _trip.Name),
            Input.Bind(() => _trip.City).Id("city").Blur().Validate(TooShort("City")),
            Validation.Message.Template(messages => P.Id("city-error")[messages[0]]).For(() => _trip.City),
            Input.Bind(() => _trip.Code).Id("code").Blur().Data("rask-keys", "F2").OnKeyDown(_ => _keys++),
            Button.Type(ButtonType.Submit).Id("save")["save"]
        ]
    ];

    private static Validate<string> TooShort(string what) =>
        value => value.Length is > 0 and < 3 ? [what + " is too short."] : [];

    private async ValueTask<IEnumerable<string>> NameIsFree(string name)
    {
        _lookups++;
        await Task.Delay(20, Current.Cancellation);

        return string.Equals(name, "Atlantis", StringComparison.Ordinal) ? ["Name is taken."] : [];
    }

    // Renders after the reader has gone on to type: sooner than the pause, later than a few keystrokes.
    private async Task TickLater()
    {
        await Task.Delay(PauseMilliseconds / 2);
        _ticks++;
    }

    private void Save(Trip trip) =>
        _saved = trip.City.Length == 0 ? trip.Name : trip.Name + "/" + trip.City;
}
