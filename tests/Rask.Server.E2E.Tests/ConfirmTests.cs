using System.Collections.Concurrent;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     <c>.Confirm("…")</c> — Livewire's <c>wire:confirm</c> — in a real browser: the browser's dialog is asked before a
///     click or a submit leaves it, and an action the reader refused sends nothing at all.
/// </summary>
/// <remarks>
///     What was sent is read off the socket, because a refused action and a lost one look the same from the page.
///     The WebAssembly host's half is <c>ConfirmWasmTests</c> in <c>Rask.Site.E2E.Tests</c>.
/// </remarks>
public sealed class ConfirmTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Fact]
    public async Task A_dismissed_confirm_sends_neither_the_click_nor_the_field_waiting_to_go_with_it()
    {
        var sent = new ConcurrentQueue<string>();
        await using var session = await HookSession.OpenAsync<ConfirmPage>(playwright, beforeLoad: Listen(sent));
        var page = session.Page;
        var asked = Answer(page, _ => null);
        await page.FillAsync("#name", "Ada");
        sent.Clear();

        await page.ClickAsync("#delete");
        await page.WaitForTimeoutAsync(300);

        Assert.Equal(["confirm:Delete it?"], asked);
        Assert.DoesNotContain(sent, frame => frame.Contains("Ada", StringComparison.Ordinal) || frame.Contains("click", StringComparison.Ordinal));
        await Expect(page.Locator("#deleted")).ToHaveTextAsync("deleted=0");
    }

    [Fact]
    public async Task An_accepted_confirm_sends_the_click_and_the_field_ahead_of_it()
    {
        await using var session = await HookSession.OpenAsync<ConfirmPage>(playwright);
        var page = session.Page;
        var asked = Answer(page, _ => "");
        await page.FillAsync("#name", "Ada");

        await page.ClickAsync("#delete");

        await Expect(page.Locator("#deleted")).ToHaveTextAsync("deleted=1 name=Ada");
        Assert.Equal(["confirm:Delete it?"], asked);
    }

    [Fact]
    public async Task The_prompt_goes_only_when_the_answer_is_typed_exactly()
    {
        await using var session = await HookSession.OpenAsync<ConfirmPage>(playwright);
        var page = session.Page;
        var typed = new Queue<string>(["delete", "DELETE"]);
        var asked = Answer(page, _ => typed.Dequeue());

        await page.ClickAsync("#wipe");
        await page.ClickAsync("#wipe");

        await Expect(page.Locator("#wiped")).ToHaveTextAsync("wiped=1");
        Assert.Equal(["prompt:Type DELETE to wipe it", "prompt:Type DELETE to wipe it"], asked);
    }

    [Fact]
    public async Task A_form_that_asks_submits_only_once_the_reader_said_yes()
    {
        await using var session = await HookSession.OpenAsync<ConfirmPage>(playwright);
        var page = session.Page;
        var answers = new Queue<string?>([null, ""]);
        var asked = Answer(page, _ => answers.Dequeue());

        await page.ClickAsync("#send");
        await page.ClickAsync("#send");

        await Expect(page.Locator("#sent")).ToHaveTextAsync("sent=1");
        Assert.Equal(["confirm:Send the order?", "confirm:Send the order?"], asked);
    }

    // Every frame the page sends over its socket, from the first.
    private static Func<IPage, Task> Listen(ConcurrentQueue<string> sent) => page =>
    {
        page.WebSocket += (_, socket) => socket.FrameSent += (_, frame) => sent.Enqueue(frame.Text ?? "");
        return Task.CompletedTask;
    };

    // Answers each dialog with what `reply` says — null dismisses it, a string accepts it (typed into a prompt) — and
    // records it as "<type>:<message>".
    private static List<string> Answer(IPage page, Func<IDialog, string?> reply)
    {
        List<string> asked = [];
        page.Dialog += (_, dialog) =>
        {
            asked.Add(dialog.Type + ":" + dialog.Message);
            _ = reply(dialog) is { } text ? dialog.AcceptAsync(text) : dialog.DismissAsync();
        };
        return asked;
    }
}

internal sealed class ConfirmOrder
{
    public string Item { get; set; } = "";
}

/// <summary>A button, a prompt and a form that each ask first, beside a field that waits for the next action.</summary>
public sealed partial class ConfirmPage : Component
{
    private readonly ConfirmOrder _order = new();
    private readonly ConfirmOrder _draft = new();
    private int _deleted;
    private int _wiped;
    private int _sent;

    protected override Component? HeadAssets => Markup.Title["confirm"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("deleted")[_deleted == 0 ? "deleted=0" : $"deleted={_deleted} name={_draft.Item}"],
        P.Id("wiped")[$"wiped={_wiped}"],
        P.Id("sent")[$"sent={_sent}"],
        Input.Bind(() => _draft.Item).Id("name"),
        Button.Id("delete").OnClick(() => _deleted++).Confirm("Delete it?")["delete"],
        Button.Id("wipe").OnClick(() => _wiped++).Confirm("Type DELETE to wipe it", expect: "DELETE")["wipe"],
        Form.Model(_order).OnSubmit(_ => _sent++).Confirm("Send the order?")[
            Button.Type(ButtonType.Submit).Id("send")["send"]
        ]
    ];
}
