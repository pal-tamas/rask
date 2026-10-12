using System.Text.Json;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Forms;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A toast that leaves by itself while the reader is typing into a field that waits, in a real browser over
///     the real socket: its timer is not the reader's next action, so nothing they typed is sent with it.
/// </summary>
/// <remarks>
///     Reported from an app whose every save says so in a toast: a row was saved, the next one opened in place,
///     and 4.5 s later the toast's own press on its close button carried the half-typed name to the server, whose
///     unique rule then spoke under a field the reader was still in. The toast's time is the page's timer, so the
///     journey stops the page's clock and runs it by hand: the keys can take a loaded machine as long as they
///     like, and the toast goes exactly when the journey says.
/// </remarks>
public sealed class ToastLeavesTypingAloneTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string ToastDialog = "[data-ui-toast] [data-ui-toast-dialog]";

    [Fact]
    public async Task A_toast_leaving_by_itself_sends_nothing_the_reader_typed_and_Save_then_carries_it_once()
    {
        await using var session = await HookSession.OpenAsync<SaveThenNewPage>(playwright, beforeLoad: KeepFramesOnATestClock);
        var page = session.Page;
        await page.ClickAsync("#ready");
        await Expect(page.Locator("#is-ready")).ToHaveTextAsync("ready");
        await page.Locator("#name").PressSequentiallyAsync("Graz");
        await PageClock.StopAsync(page);
        await page.ClickAsync("#save");
        await Expect(page.Locator(ToastDialog)).ToHaveCountAsync(1);
        await page.ClickAsync("#new");
        await Expect(page.Locator("#name")).ToHaveValueAsync("");

        var before = (await SentAsync(page)).Length;
        await page.Locator("#name").ClickAsync();
        await page.Locator("#name").PressSequentiallyAsync("Graz");
        await page.Clock.RunForAsync(SaveThenNewPage.ToastMilliseconds + 2_000);
        await Expect(page.Locator(ToastDialog)).ToHaveCountAsync(0);
        var whileTheToastLeft = (await SentAsync(page))[before..];
        var messagesWhileTyping = await page.Locator("#name-error").CountAsync();
        var modelWhileTyping = await page.Locator("#model").TextContentAsync();
        var focused = await page.EvaluateAsync<string>("() => document.activeElement.id");
        var typed = await page.Locator("#name").InputValueAsync();
        var beforeSave = (await SentAsync(page)).Length;
        await page.ClickAsync("#save");
        await Expect(page.Locator("#name-error")).ToHaveTextAsync("That name is taken.");
        var withSave = (await SentAsync(page))[beforeSave..];

        // The toast's own press, alone and saying whose doing it was: no `change` rode ahead of it.
        Assert.Equal(["click~"], whileTheToastLeft);
        Assert.Equal(0, messagesWhileTyping);
        Assert.Equal("name=", modelWhileTyping);
        Assert.Equal(("name", "Graz"), (focused, typed));
        Assert.Equal(["change:Graz+submit"], withSave);
    }

    // Every frame the socket carries, as its event types joined with `+`: a `change` with its value, and an
    // event the runtime raised by itself with a `~` after it.
    private static async Task KeepFramesOnATestClock(IPage page)
    {
        await page.AddInitScriptAsync(
            """
            window.__frames = [];
            const send = WebSocket.prototype.send;
            WebSocket.prototype.send = function (data) {
                const frame = JSON.parse(data);
                const said = (frame.type === "batch" ? frame.events : [frame]).map(function (e) {
                    return e.type + (e.type === "change" ? ":" + e.value : "") + (e.unasked ? "~" : "");
                });
                if (frame.type !== "hello") window.__frames.push(said.join("+"));
                return send.call(this, data);
            };
            """);
        await PageClock.Install(page);
    }

    private static async Task<string[]> SentAsync(IPage page) =>
        JsonSerializer.Deserialize<string[]>(await page.EvaluateAsync<string>("() => JSON.stringify(window.__frames)"))!;
}

/// <summary>The reported journey: a name with a unique rule, a save that says so in a toast, and the next one opened in place.</summary>
public sealed partial class SaveThenNewPage : Component
{
    internal const int ToastMilliseconds = 2_000;

    private readonly List<string> _saved = [];
    private Draft _draft = new();
    private bool _editing = true;
    private bool _ready;

    protected override Component? HeadAssets => Markup.Title["save then new"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() => Div[
        P.Id("is-ready")[_ready ? "ready" : "loading"],
        Button.Id("ready").OnClick(() => _ready = true)["ready"],
        Main[
            P.Id("model")[$"name={_draft.Name}"],
            _editing
                ? Form.Model(_draft).OnSubmit(Save).NoValidate(true)[
                    Ui.Input.Bind(() => _draft.Name).Id("name").Validate(Free).ShowValidation(false),
                    Validation.Message.Template(messages => P.Id("name-error")[messages[0]]).For(() => _draft.Name),
                    Ui.Button.Submit.Id("save")["Save"]
                ]
                : Ui.Button.Id("new").OnClick(New)["New"]
        ],
        Ui.Toast
    ];

    private IEnumerable<string> Free(string name) => _saved.Contains(name) ? ["That name is taken."] : [];

    private void Save(Draft draft)
    {
        _saved.Add(draft.Name);
        _editing = false;
        Toast.Success("Saved.").For(TimeSpan.FromMilliseconds(ToastMilliseconds));
    }

    private void New()
    {
        _draft = new Draft();
        _editing = true;
    }

    private sealed class Draft
    {
        public string Name { get; set; } = string.Empty;
    }
}
