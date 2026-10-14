using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // small test pages; their <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     #1245: a chat box empties itself. A bound field that waits for the next action goes with the submit, whose
///     handler sets it to "", and the browser shows what the page says — the field is still focused, and the last
///     render had it empty too. A render that knows nothing newer still leaves what is being typed alone.
/// </summary>
public sealed class EmptiedOnSubmitTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private Task<HookSession> OpenAsync(bool kit) =>
        kit
            ? HookSession.OpenAsync<ChatKitPage>(playwright, beforeLoad: KeepFrames)
            : HookSession.OpenAsync<ChatCorePage>(playwright, beforeLoad: KeepFrames);

    // Every frame the server sends, kept as the page received it.
    private static Task KeepFrames(IPage page) => page.AddInitScriptAsync(
        """
        window.__frames = [];
        const listen = WebSocket.prototype.addEventListener;
        WebSocket.prototype.addEventListener = function (type, listener, options) {
            if (type !== "message") return listen.call(this, type, listener, options);
            return listen.call(this, type, function (e) { window.__frames.push(String(e.data)); listener(e); }, options);
        };
        """);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Enter_in_a_chat_box_sends_the_message_and_empties_the_box_it_is_still_in(bool kit)
    {
        await using var session = await OpenAsync(kit);
        var page = session.Page;
        await Ready(page);

        await page.Locator("#message").PressSequentiallyAsync("hello");
        await page.Locator("#message").PressAsync("Enter");

        await Expect(page.Locator("#sent")).ToHaveTextAsync("sent=hello|");
        await Expect(page.Locator("#message")).ToHaveValueAsync("");
        await Expect(page.Locator("#message")).ToBeFocusedAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_textarea_the_submit_empties_is_empty_and_a_second_message_goes_the_same_way(bool kit)
    {
        await using var session = await OpenAsync(kit);
        var page = session.Page;
        await Ready(page);

        await page.Locator("#notes").PressSequentiallyAsync("first");
        await page.ClickAsync("#send");
        await Expect(page.Locator("#sent")).ToHaveTextAsync("sent=|first");
        await page.Locator("#notes").PressSequentiallyAsync("second");
        await page.ClickAsync("#send");

        await Expect(page.Locator("#sent")).ToHaveTextAsync("sent=|second");
        await Expect(page.Locator("#notes")).ToHaveValueAsync("");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_password_the_submit_empties_is_empty_and_the_page_never_said_its_text_beside_it(bool kit)
    {
        await using var session = await OpenAsync(kit);
        var page = session.Page;
        await Ready(page);
        await page.Locator("#secret").PressSequentiallyAsync("hunter2-correct-horse");
        await page.Locator("#secret").PressAsync("Enter");

        await Expect(page.Locator("#sent")).ToHaveTextAsync("sent=|");
        await Expect(page.Locator("#secret")).ToHaveValueAsync("");
        // What the page wrote beside the field is a count: "<n>:<text>" is how a non-secret word is written.
        Assert.DoesNotContain(":hunter2-correct-horse", string.Concat(await page.EvaluateAsync<string[]>("() => window.__frames")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_action_that_keeps_the_value_leaves_what_was_typed(bool kit)
    {
        await using var session = await OpenAsync(kit);
        var page = session.Page;
        await Ready(page);

        await page.Locator("#message").PressSequentiallyAsync("kept");
        await page.ClickAsync("#peek");

        await Expect(page.Locator("#peeked")).ToHaveTextAsync("peeked=kept");
        await Expect(page.Locator("#message")).ToHaveValueAsync("kept");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task What_is_typed_after_the_box_was_emptied_survives_a_render_nobody_asked_for(bool kit)
    {
        await using var session = await OpenAsync(kit);
        var page = session.Page;
        await Ready(page);
        await page.Locator("#message").PressSequentiallyAsync("hi");
        await page.Locator("#message").PressAsync("Enter");
        await Expect(page.Locator("#message")).ToHaveValueAsync("");

        await page.ClickAsync("#later");
        await page.Locator("#message").PressSequentiallyAsync("next");
        await Expect(page.Locator("#ticked")).ToHaveTextAsync("ticks=1");

        await Expect(page.Locator("#message")).ToHaveValueAsync("next");
        await page.Locator("#message").PressAsync("Enter");
        await Expect(page.Locator("#sent")).ToHaveTextAsync("sent=next|");
    }

    // The page is live once a press reaches the server and comes back.
    private static async Task Ready(IPage page)
    {
        await page.ClickAsync("#ready");
        await Expect(page.Locator("#is-ready")).ToHaveTextAsync("ready");
    }
}

/// <summary>What both chat pages hold.</summary>
public sealed class ChatDraft
{
    public string Message { get; set; } = "";

    public string Notes { get; set; } = "";

    public string Secret { get; set; } = "";
}

/// <summary>A chat box of plain Core controls.</summary>
public sealed partial class ChatCorePage : Component
{
    private readonly ChatDraft _draft = new();
    private string _sent = "";
    private string _peeked = "";
    private int _ticks;
    private bool _ready;

    protected override Component? HeadAssets => Markup.Title["emptied on submit, core"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("sent")[$"sent={_sent}"],
        P.Id("peeked")[$"peeked={_peeked}"],
        P.Id("ticked")[$"ticks={_ticks}"],
        P.Id("is-ready")[_ready ? "ready" : "loading"],
        Button.Id("ready").OnClick(() => _ready = true)["ready"],
        Button.Id("later").OnClick(TickLater)["later"],
        Button.Id("peek").OnClick(() => _peeked = _draft.Message)["peek"],
        Form.Model(_draft).OnSubmit(Send).NoValidate(true)[
            Input.Bind(() => _draft.Message).Id("message"),
            Textarea.Bind(() => _draft.Notes).Id("notes"),
            Input.Bind(() => _draft.Secret).Type(InputType.Password).Id("secret"),
            Button.Type(ButtonType.Submit).Id("send")["send"]
        ]
    ];

    private async Task TickLater()
    {
        await Task.Delay(300);
        _ticks++;
    }

    private void Send(ChatDraft draft)
    {
        _sent = $"{draft.Message}|{draft.Notes}";
        draft.Message = "";
        draft.Notes = "";
        draft.Secret = "";
    }
}

/// <summary>The same chat box drawn with the kit's controls.</summary>
public sealed partial class ChatKitPage : Component
{
    private readonly ChatDraft _draft = new();
    private string _sent = "";
    private string _peeked = "";
    private int _ticks;
    private bool _ready;

    protected override Component? HeadAssets => Markup.Title["emptied on submit, kit"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("sent")[$"sent={_sent}"],
        P.Id("peeked")[$"peeked={_peeked}"],
        P.Id("ticked")[$"ticks={_ticks}"],
        P.Id("is-ready")[_ready ? "ready" : "loading"],
        Button.Id("ready").OnClick(() => _ready = true)["ready"],
        Button.Id("later").OnClick(TickLater)["later"],
        Button.Id("peek").OnClick(() => _peeked = _draft.Message)["peek"],
        Form.Model(_draft).OnSubmit(Send).NoValidate(true)[
            Ui.Input.Bind(() => _draft.Message).Id("message"),
            Ui.Textarea.Bind(() => _draft.Notes).Id("notes"),
            Ui.Input.Bind(() => _draft.Secret).Type(InputType.Password).Id("secret"),
            Ui.Button.Submit.Id("send")["send"]
        ]
    ];

    private async Task TickLater()
    {
        await Task.Delay(300);
        _ticks++;
    }

    private void Send(ChatDraft draft)
    {
        _sent = $"{draft.Message}|{draft.Notes}";
        draft.Message = "";
        draft.Notes = "";
        draft.Secret = "";
    }
}
