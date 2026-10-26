using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     <c>Ui.Composer</c> on a Server page: the keys that submit its form (<c>data-rask-submit-keys</c>) and the box
///     that grows with its text (<c>data-rask-grow</c>), in a real browser.
/// </summary>
/// <remarks>
///     Flux UI's composer, measured on its page: Enter is a new line and Cmd+Enter or Ctrl+Enter submits; with
///     <c>submit="enter"</c> Enter submits and Shift+Enter is a new line. The box grows a line at a time from
///     <c>rows</c> to <c>max-rows</c> and scrolls from there.
/// </remarks>
public sealed class UiComposerHookTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Sent = "() => document.getElementById('sent').textContent";

    [Fact]
    public async Task Ctrl_Enter_sends_the_form_with_what_was_typed_and_Enter_alone_starts_a_new_line()
    {
        await using var session = await HookSession.OpenAsync<ComposerHookPage>(playwright);
        var page = session.Page;
        await page.FocusAsync("#f-prompt");
        await page.Keyboard.TypeAsync("Hello");
        await page.Keyboard.PressAsync("Enter");
        await page.Keyboard.TypeAsync("there");

        await page.Keyboard.PressAsync("Control+Enter");

        await page.WaitForFunctionAsync("() => document.getElementById('sent').textContent === 'Hello\\nthere'", null, new() { Timeout = 5_000 });
        Assert.Equal("Hello\nthere", await page.InputValueAsync("#f-prompt"));
    }

    [Fact]
    public async Task Meta_Enter_sends_the_form_too()
    {
        await using var session = await HookSession.OpenAsync<ComposerHookPage>(playwright);
        var page = session.Page;
        await page.FocusAsync("#f-prompt");
        await page.Keyboard.TypeAsync("Hi");

        await page.Keyboard.PressAsync("Meta+Enter");

        await page.WaitForFunctionAsync("() => document.getElementById('sent').textContent === 'Hi'", null, new() { Timeout = 5_000 });
        Assert.Equal("Hi", await page.InputValueAsync("#f-prompt"));
    }

    [Fact]
    public async Task With_Enter_asked_for_Enter_sends_and_Shift_Enter_starts_a_new_line()
    {
        await using var session = await HookSession.OpenAsync<ComposerHookPage>(playwright);
        var page = session.Page;
        await page.FocusAsync("#f-quick");
        await page.Keyboard.TypeAsync("One");
        await page.Keyboard.PressAsync("Shift+Enter");
        await page.Keyboard.TypeAsync("Two");

        await page.Keyboard.PressAsync("Enter");

        await page.WaitForFunctionAsync("() => document.getElementById('sent').textContent === 'One\\nTwo'", null, new() { Timeout = 5_000 });
        Assert.Equal("One\nTwo", await page.InputValueAsync("#f-quick"));
    }

    [Fact]
    public async Task The_box_grows_a_line_at_a_time_to_its_last_line_and_scrolls_from_there()
    {
        await using var session = await HookSession.OpenAsync<ComposerHookPage>(playwright);
        var page = session.Page;
        await page.FocusAsync("#f-prompt");
        var rest = await page.EvaluateAsync<int>("() => document.getElementById('f-prompt').offsetHeight");
        var line = await page.EvaluateAsync<int>("() => parseFloat(getComputedStyle(document.getElementById('f-prompt')).lineHeight)");
        var heights = new List<string>();

        for (var enter = 0; enter < 4; enter++)
        {
            await page.Keyboard.PressAsync("Enter");
            heights.Add(await page.EvaluateAsync<string>("() => { const t = document.getElementById('f-prompt'); return t.offsetHeight + ' ' + getComputedStyle(t).overflowY; }"));
        }

        // Two lines fit the two rows; the third grows it a line; the fourth passes the most it may show.
        var grown = rest + line;
        Assert.Equal([$"{rest} hidden", $"{grown} hidden", $"{grown} auto", $"{grown} auto"], heights);
        Assert.Equal(string.Empty, await page.EvaluateAsync<string>(Sent));
    }

    [Fact]
    public async Task A_placeholder_that_wraps_on_a_phone_makes_the_box_as_tall_as_it_from_the_first_paint()
    {
        const string Height = "() => document.getElementById('f-note').offsetHeight";
        await using var desktop = await HookSession.OpenAsync<ComposerHookPage>(playwright);
        await using var phone = await HookSession.OpenAsync<ComposerHookPage>(
            playwright, new() { ViewportSize = new() { Width = 390, Height = 844 } });

        // Fitted once the hooks have loaded: the box then says its height.
        const string Fitted = "() => document.getElementById('f-note').style.height !== ''";
        await desktop.Page.WaitForFunctionAsync(Fitted, null, new() { Timeout = 5_000 });
        await phone.Page.WaitForFunctionAsync(Fitted, null, new() { Timeout = 5_000 });

        const string Line = "() => parseFloat(getComputedStyle(document.getElementById('f-note')).lineHeight)";
        var wide = await desktop.Page.EvaluateAsync<int>(Height);
        var wideLine = await desktop.Page.EvaluateAsync<int>(Line);
        var narrow = await phone.Page.EvaluateAsync<int>(Height);
        var narrowLine = await phone.Page.EvaluateAsync<int>(Line);

        // One row on a desktop; on a phone the placeholder takes a second line, and so does the box — the same
        // padding and border around both.
        Assert.Equal(wide - wideLine, narrow - (2 * narrowLine));
    }
}

/// <summary>Two composers in their forms: Ctrl/Cmd + Enter, three lines at most; and Enter.</summary>
public sealed partial class ComposerHookPage : Component
{
    private readonly Chat _chat = new();
    private string _sent = string.Empty;

    protected override Component? HeadAssets => [Markup.Title["composer hook"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        Form.Model(_chat).OnSubmit(_ => _sent = _chat.Prompt)[
            Ui.Composer.Bind(() => _chat.Prompt).Label("Prompt").MaxRows(3)
        ],
        Form.Model(_chat).OnSubmit(_ => _sent = _chat.Quick)[
            Ui.Composer.Bind(() => _chat.Quick).Label("Quick").Enter
        ],
        Form.Model(_chat)[
            Ui.Composer.Bind(() => _chat.Note).Label("Note").Inline().Rows(1)
                .Placeholder("Write a note for the whole team to read before tomorrow")
        ],
        P.Id("sent").Style("white-space:pre")[_sent]
    ];

    private sealed class Chat
    {
        public string Prompt { get; set; } = string.Empty;

        public string Quick { get; set; } = string.Empty;

        public string Note { get; set; } = string.Empty;
    }
}
