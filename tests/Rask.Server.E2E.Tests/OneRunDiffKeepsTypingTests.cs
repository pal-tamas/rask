using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test
#pragma warning disable RASK022 // rows nothing keys are the subject of the last journey

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A child that comes or goes among siblings that still match is answered with a diff that touches nothing
///     else: the field the reader is typing in stays the node it was.
/// </summary>
/// <remarks>
///     <para>
///         Such a change used to be answered with the whole page, morphed by position: with a callout arriving
///         above two fields, the first field's node was turned into the callout and a new field was built at the
///         end — focus, the caret and anything typed into an input the server does not bind were gone.
///     </para>
///     <para>
///         The fields here are deliberately NOT bound: nothing sends what is typed into them, so nothing but
///         the node itself holds it. Each journey marks the node before the change and asks for the same one
///         after it.
///     </para>
/// </remarks>
public sealed class OneRunDiffKeepsTypingTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Mark = "id => { window.__kept = document.getElementById(id); }";
    private const string Kept = "id => window.__kept === document.getElementById(id) && window.__kept.isConnected";

    private async Task<(HookSession Session, SocketFrames Frames)> OpenAsync()
    {
        SocketFrames? frames = null;
        var session = await HookSession.OpenAsync<OneRunPage>(playwright, beforeLoad: page =>
        {
            frames = SocketFrames.Of(page);
            return Task.CompletedTask;
        });
        await Expect(session.Page.Locator("#ready")).ToHaveTextAsync("ready");
        frames!.Clear();

        return (session, frames);
    }

    [Fact]
    public async Task A_callout_arriving_above_the_field_being_typed_in_leaves_its_focus_caret_and_text_and_the_buttons_below_run_their_own_handlers()
    {
        var (session, frames) = await OpenAsync();
        await using var _ = session;
        var page = session.Page;
        await page.EvaluateAsync(Mark, "name");

        await page.ClickAsync("#note-slowly");
        await page.ClickAsync("#name");
        await page.Keyboard.TypeAsync("Range one");
        await page.Keyboard.PressAsync("ArrowLeft");
        await page.Keyboard.PressAsync("ArrowLeft");
        await page.Keyboard.PressAsync("ArrowLeft");
        await Expect(page.Locator("#note")).ToBeVisibleAsync();
        var focus = await session.FocusAsync();
        var caret = await page.EvaluateAsync<int>("() => document.getElementById('name').selectionStart");
        var kept = await page.EvaluateAsync<bool>(Kept, "name");
        await page.Keyboard.TypeAsync("number ");
        await page.ClickAsync("#archive");
        await page.ClickAsync("#delete");
        await page.ClickAsync("#archive");

        await Expect(page.Locator("#name")).ToHaveValueAsync("Range number one");
        await Expect(page.Locator("#pressed")).ToHaveTextAsync("archive=2 delete=1");
        Assert.Equal("name", focus);
        Assert.Equal(6, caret);
        Assert.True(kept, "the field is not the node it was");
        Assert.True(frames.FullPages.Count == 0, frames.Trace());
    }

    [Fact]
    public async Task A_callout_leaving_from_above_the_fields_leaves_them_as_they_were_and_the_buttons_below_run_their_own_handlers()
    {
        var (session, frames) = await OpenAsync();
        await using var _ = session;
        var page = session.Page;
        await page.ClickAsync("#note-slowly");
        await Expect(page.Locator("#note")).ToBeVisibleAsync();
        await page.FillAsync("#email", "ada@example.com");
        await page.EvaluateAsync(Mark, "email");

        await page.ClickAsync("#dismiss");
        await Expect(page.Locator("#note")).ToHaveCountAsync(0);
        var kept = await page.EvaluateAsync<bool>(Kept, "email");
        await page.ClickAsync("#delete");
        await page.ClickAsync("#archive");

        await Expect(page.Locator("#email")).ToHaveValueAsync("ada@example.com");
        await Expect(page.Locator("#pressed")).ToHaveTextAsync("archive=1 delete=1");
        Assert.True(kept, "the field is not the node it was");
        Assert.True(frames.FullPages.Count == 0, frames.Trace());
    }

    [Fact]
    public async Task An_error_text_arriving_under_a_field_and_leaving_again_leaves_what_was_typed_in_it()
    {
        var (session, frames) = await OpenAsync();
        await using var _ = session;
        var page = session.Page;
        await page.FillAsync("#email", "ada@");
        await page.EvaluateAsync(Mark, "email");
        await page.EvaluateAsync("() => { window.__hint = document.getElementById('email-hint'); }");

        await page.ClickAsync("#refuse");
        await Expect(page.Locator("#email-error")).ToHaveTextAsync("Enter an email address.");
        var keptWithTheError = await page.EvaluateAsync<bool>(Kept, "email");
        var hintKept = await page.EvaluateAsync<bool>("() => window.__hint === document.getElementById('email-hint')");
        await page.ClickAsync("#accept");
        await Expect(page.Locator("#email-error")).ToHaveCountAsync(0);
        var keptWithoutIt = await page.EvaluateAsync<bool>(Kept, "email");

        await Expect(page.Locator("#email")).ToHaveValueAsync("ada@");
        await Expect(page.Locator("#email-hint")).ToHaveTextAsync("Never shared.");
        Assert.True(keptWithTheError, "the field is not the node it was once the error arrived");
        Assert.True(hintKept, "the hint below the error was turned into the error, and a new hint built after it");
        Assert.True(keptWithoutIt, "the field is not the node it was once the error left");
        Assert.True(frames.FullPages.Count == 0, frames.Trace());
    }

    [Fact]
    public async Task A_row_arriving_at_the_top_of_an_unkeyed_list_leaves_the_rows_below_the_nodes_they_were()
    {
        var (session, frames) = await OpenAsync();
        await using var _ = session;
        var page = session.Page;
        await page.FillAsync("#rows li:nth-child(1) input", "first note");
        await page.FillAsync("#rows li:nth-child(2) input", "second note");
        await page.EvaluateAsync("() => { window.__rows = [...document.querySelectorAll('#rows li')]; }");

        await page.ClickAsync("#add-row");
        await Expect(page.Locator("#rows li")).ToHaveCountAsync(3);
        var kept = await page.EvaluateAsync<bool>(
            "() => { const now = [...document.querySelectorAll('#rows li')]; return now[1] === window.__rows[0] && now[2] === window.__rows[1]; }");

        await Expect(page.Locator("#rows li:nth-child(1) span")).ToHaveTextAsync("row 3");
        await Expect(page.Locator("#rows li:nth-child(1) input")).ToHaveValueAsync(string.Empty);
        await Expect(page.Locator("#rows li:nth-child(2) input")).ToHaveValueAsync("first note");
        await Expect(page.Locator("#rows li:nth-child(3) input")).ToHaveValueAsync("second note");
        Assert.True(kept, "the rows below are not the nodes they were");
        Assert.True(frames.FullPages.Count == 0, frames.Trace());
    }
}

/// <summary>Fields nothing binds, with a callout that comes and goes above them, an error under one, and rows nothing keys.</summary>
public sealed partial class OneRunPage : Component
{
    private readonly List<string> _rows = ["row 1", "row 2"];
    private bool _noted;
    private bool _refused;
    private int _archived;
    private int _deleted;

    protected override Component? HeadAssets => Markup.Title["one run"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() => Div[
        Section.Id("fields")[
            // Same tag as the fields below it, and a handler of its own: every handler after it is renumbered.
            _noted ? Div.Id("note").Class("note")["That name is taken.", Button.Id("dismiss").OnClick(() => _noted = false)["Dismiss"]] : null,
            Div.Class("field")[Input.Value(string.Empty).Id("name")],
            Div.Class("field")[
                Input.Value(string.Empty).Id("email"),
                _refused ? P.Id("email-error")["Enter an email address."] : null,
                P.Id("email-hint")["Never shared."]
            ],
            Button.Id("archive").OnClick(() => _archived++)["Archive"],
            Button.Id("delete").OnClick(() => _deleted++)["Delete"]
        ],
        P.Id("pressed")[$"archive={_archived} delete={_deleted}"],
        Ul.Id("rows")[_rows.Select(row => (Component)Li[Span[row], Input.Value(string.Empty)])],
        Button.Id("note-slowly").OnClick(NoteSlowly)["note"],
        Button.Id("refuse").OnClick(() => _refused = true)["refuse"],
        Button.Id("accept").OnClick(() => _refused = false)["accept"],
        Button.Id("add-row").OnClick(() => _rows.Insert(0, $"row {_rows.Count + 1}"))["add row"],
        P.Id("ready")["ready"]
    ];

    private async Task NoteSlowly()
    {
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        _noted = true;
    }
}
