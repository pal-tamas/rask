using System.Collections.Concurrent;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     <c>Ui.Confirm</c> in a real browser: with it on the page, <c>.Confirm("…")</c> asks in that dialog and not in
///     the browser's <c>confirm</c> or <c>prompt</c>, every way of closing it but Yes calls the action off, and Yes
///     lets the very press or submit go — with nothing sent while the reader is being asked.
/// </summary>
/// <remarks>
///     The page is <see cref="ConfirmActions" /> with the element added, so <c>ConfirmTests</c> beside
///     this is the same actions without it. Every test fails on a browser dialog: with the element there, one
///     appearing is the bug.
/// </remarks>
public sealed class ConfirmDialogTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Dialog = "#ui-confirm";
    private const string Yes = Dialog + " [data-rask-confirm-dialog=go]";
    private const string Field = Dialog + " [data-rask-confirm-dialog=field] input";

    [Fact]
    public async Task A_press_opens_the_dialog_with_the_heading_over_the_question_and_sends_nothing()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.FillAsync("#name", "Ada");
        session.Sent.Clear();

        await page.ClickAsync("#delete");
        await Expect(page.Locator(Dialog)).ToBeVisibleAsync();
        await page.WaitForTimeoutAsync(300);

        await Expect(page.Locator(Dialog + " [data-ui-heading]")).ToHaveTextAsync("Megerősítés");
        await Expect(page.Locator(Dialog + " [data-rask-confirm-dialog=message]")).ToHaveTextAsync("Delete it?");
        await Expect(page.Locator(Dialog + " [data-rask-confirm-dialog=field]")).ToBeHiddenAsync();
        Assert.True(await page.Locator(Dialog).EvaluateAsync<bool>("d => d.matches(':modal') && d.contains(document.activeElement)"));
        Assert.Empty(session.Sent);
        Assert.Empty(session.BrowserDialogs);
    }

    [Fact]
    public async Task A_question_with_its_own_heading_shows_it_and_the_next_one_shows_the_layouts_again()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        var heading = page.Locator(Dialog + " [data-rask-confirm-dialog=heading]");
        session.Sent.Clear();

        await page.ClickAsync("#remove");
        await Expect(page.Locator(Dialog)).ToBeVisibleAsync();
        var own = await heading.TextContentAsync();
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator(Dialog)).ToBeHiddenAsync();
        await page.ClickAsync("#delete");
        await Expect(page.Locator(Dialog)).ToBeVisibleAsync();

        Assert.Equal("Cég törlése", own);
        await Expect(heading).ToHaveTextAsync("Megerősítés");
        await Expect(page.Locator(Dialog + " [data-rask-confirm-dialog=message]")).ToHaveTextAsync("Delete it?");
        Assert.Empty(session.Sent);
    }

    [Theory]
    [InlineData("no")]
    [InlineData("x")]
    [InlineData("escape")]
    [InlineData("outside")]
    public async Task Every_way_of_closing_the_dialog_but_Yes_calls_the_action_off_and_sends_nothing(string way)
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.FillAsync("#name", "Ada");
        session.Sent.Clear();

        await page.ClickAsync("#delete");
        await Expect(page.Locator(Dialog)).ToBeVisibleAsync();
        await CloseAsync(page, way);
        await Expect(page.Locator(Dialog)).ToBeHiddenAsync();
        await page.WaitForTimeoutAsync(300);

        Assert.Empty(session.Sent);
        await Expect(page.Locator("#deleted")).ToHaveTextAsync("deleted=0");
        Assert.Empty(session.BrowserDialogs);
    }

    [Fact]
    public async Task Yes_runs_the_handler_with_the_field_waiting_to_go_ahead_of_it()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.FillAsync("#name", "Ada");

        await page.ClickAsync("#delete");
        await page.ClickAsync(Yes);

        await Expect(page.Locator("#deleted")).ToHaveTextAsync("deleted=1 name=Ada");
        await Expect(page.Locator(Dialog)).ToBeHiddenAsync();
        Assert.Empty(session.BrowserDialogs);
    }

    [Fact]
    public async Task Focus_returns_to_the_control_that_was_pressed()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.FocusAsync("#delete");

        await page.Keyboard.PressAsync("Enter");
        await Expect(page.Locator(Dialog)).ToBeVisibleAsync();
        var focusInside = await page.Locator(Dialog).EvaluateAsync<bool>("d => d.contains(document.activeElement)");
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator(Dialog)).ToBeHiddenAsync();

        Assert.True(focusInside);
        Assert.Equal("delete", await session.Session.FocusAsync());
    }

    [Fact]
    public async Task With_expect_Yes_waits_for_the_answer_typed_exactly_case_and_all()
    {
        await using var session = await OpenAsync();
        var page = session.Page;

        await page.ClickAsync("#wipe");
        await Expect(page.Locator(Field)).ToBeFocusedAsync();
        var disabledAtFirst = await page.Locator(Yes).IsDisabledAsync();
        await page.Keyboard.TypeAsync("delete");
        var disabledInAnotherCase = await page.Locator(Yes).IsDisabledAsync();
        await page.Keyboard.PressAsync("Enter");
        var openAfterEnterOnAMismatch = await page.Locator(Dialog).IsVisibleAsync();
        await page.FillAsync(Field, "DELETE");
        var disabledOnTheAnswer = await page.Locator(Yes).IsDisabledAsync();
        await page.ClickAsync(Yes);

        await Expect(page.Locator("#wiped")).ToHaveTextAsync("wiped=1");
        Assert.True(disabledAtFirst);
        Assert.True(disabledInAnotherCase);
        Assert.True(openAfterEnterOnAMismatch);
        Assert.False(disabledOnTheAnswer);
        Assert.Empty(session.BrowserDialogs);
    }

    [Fact]
    public async Task Enter_on_the_typed_answer_is_Yes_and_the_field_is_empty_when_asked_again()
    {
        await using var session = await OpenAsync();
        var page = session.Page;

        await page.ClickAsync("#wipe");
        await page.Keyboard.TypeAsync("DELETE");
        await page.Keyboard.PressAsync("Enter");
        await Expect(page.Locator("#wiped")).ToHaveTextAsync("wiped=1");
        await page.ClickAsync("#wipe");
        await Expect(page.Locator(Dialog)).ToBeVisibleAsync();

        await Expect(page.Locator(Field)).ToHaveValueAsync("");
        Assert.True(await page.Locator(Yes).IsDisabledAsync());
        Assert.Empty(session.BrowserDialogs);
    }

    [Fact]
    public async Task A_plain_question_after_a_typed_one_shows_no_field_and_Yes_may_be_pressed()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.ClickAsync("#wipe");
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator(Dialog)).ToBeHiddenAsync();

        await page.ClickAsync("#delete");

        await Expect(page.Locator(Dialog + " [data-rask-confirm-dialog=field]")).ToBeHiddenAsync();
        Assert.False(await page.Locator(Yes).IsDisabledAsync());
        Assert.Empty(session.BrowserDialogs);
    }

    [Fact]
    public async Task A_form_that_asks_submits_only_on_Yes_and_holds_only_then()
    {
        await using var session = await OpenAsync();
        var page = session.Page;

        await page.ClickAsync("#send");
        await Expect(page.Locator(Dialog)).ToBeVisibleAsync();
        var asked = await page.TextContentAsync(Dialog + " [data-rask-confirm-dialog=message]");
        var heldWhileAsked = await page.Locator("#send").IsDisabledAsync();
        await CloseAsync(page, "no");
        await page.WaitForTimeoutAsync(300);
        var sentAfterNo = await page.TextContentAsync("#sent");
        await page.ClickAsync("#send");
        await page.ClickAsync(Yes);

        await Expect(page.Locator("#sent")).ToHaveTextAsync("sent=1");
        Assert.Equal("Send the order?", asked);
        Assert.False(heldWhileAsked);
        Assert.Equal("sent=0", sentAfterNo);
        Assert.Empty(session.BrowserDialogs);
    }

    [Fact]
    public async Task Without_the_dialog_on_the_page_the_browser_asks_though_the_hooks_are_there()
    {
        await using var session = await HookSession.OpenAsync<ConfirmFallbackPage>(playwright);
        var page = session.Page;
        List<string> asked = [];
        page.Dialog += (_, dialog) =>
        {
            asked.Add(dialog.Type + ":" + dialog.Message);
            _ = dialog.AcceptAsync();
        };
        await session.HooksLoadedAsync();

        await page.ClickAsync("#delete");

        await Expect(page.Locator("#deleted")).ToHaveTextAsync("deleted=1 name=");
        Assert.Equal(["confirm:Delete it?"], asked);
    }

    private static async Task CloseAsync(IPage page, string way)
    {
        switch (way)
        {
            case "no":
                await page.ClickAsync(Dialog + " .flex button:not([data-rask-confirm-dialog])");
                break;
            case "x":
                await page.ClickAsync(Dialog + " button[aria-label='Close modal']");
                break;
            case "escape":
                await page.Keyboard.PressAsync("Escape");
                break;
            default:
                // The backdrop: a corner of the viewport, well outside the centred panel.
                await page.Mouse.ClickAsync(5, 690);
                break;
        }
    }

    private async Task<DialogSession> OpenAsync()
    {
        var sent = new ConcurrentQueue<string>();
        var session = await HookSession.OpenAsync<ConfirmDialogPage>(playwright, beforeLoad: page =>
        {
            page.WebSocket += (_, socket) => socket.FrameSent += (_, frame) => sent.Enqueue(frame.Text ?? "");
            return Task.CompletedTask;
        });
        List<string> browserDialogs = [];
        session.Page.Dialog += (_, dialog) =>
        {
            browserDialogs.Add(dialog.Type + ":" + dialog.Message);
            _ = dialog.DismissAsync();
        };
        await session.HooksLoadedAsync();
        return new DialogSession(session, browserDialogs, sent);
    }

    private sealed record DialogSession(HookSession Session, List<string> BrowserDialogs, ConcurrentQueue<string> Sent) : IAsyncDisposable
    {
        public IPage Page => Session.Page;

        public ValueTask DisposeAsync() => Session.DisposeAsync();
    }
}

/// <summary><see cref="ConfirmActions" />, with the kit's dialog placed beside them as a layout would.</summary>
public sealed partial class ConfirmDialogPage : Component
{
    protected override Component? HeadAssets => Markup.Title["confirm dialog"];

    protected override string? HtmlLang => "hu";

    protected override Component? Render() =>
    [
        ConfirmActions,
        Ui.Confirm.Heading("Megerősítés").No("Nem").Yes("Igen")
    ];
}

/// <summary><see cref="ConfirmActions" /> beside a kit modal that loads the hooks, and no <c>Ui.Confirm</c>.</summary>
public sealed partial class ConfirmFallbackPage : Component
{
    protected override Component? HeadAssets => Markup.Title["confirm fallback"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        ConfirmActions,
        Ui.ConfirmLeave
    ];
}
