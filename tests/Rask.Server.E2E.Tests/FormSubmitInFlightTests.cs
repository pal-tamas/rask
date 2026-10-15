using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a one-form test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A form that is submitting takes no second submit, in a real browser: Livewire's <c>wire:submit</c> rule.
/// </summary>
/// <remarks>
///     <c>FormSubmittingTests</c> proves the module's rules in node. What only a browser proves is the wiring: the
///     Server runtime holds the form at the press, before the 200 ms spinner delay, drops a second submit by
///     mouse or by Enter, and lets go on that submit's ack — not on the render that arrives before it.
/// </remarks>
public sealed class FormSubmitInFlightTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Fact]
    public async Task A_fast_double_click_on_a_submit_runs_the_handler_once()
    {
        await using var session = await HookSession.OpenAsync<InFlightFormPage>(playwright, beforeLoad: CountSubmits);
        var page = session.Page;
        await Ready(page);

        await page.DblClickAsync("#save");

        await Expect(page.Locator("#saves")).ToHaveTextAsync("saves=1");
        await Expect(page.Locator("#save")).ToBeEnabledAsync();
        await page.WaitForTimeoutAsync(500);
        await Expect(page.Locator("#saves")).ToHaveTextAsync("saves=1");
        Assert.Equal(1, await SubmitsAsync(page));
    }

    [Fact]
    public async Task While_the_reply_is_held_the_button_is_disabled_and_the_field_readonly_and_a_second_submit_is_dropped()
    {
        await using var session = await HookSession.OpenAsync<InFlightFormPage>(playwright, beforeLoad: CountSubmits);
        var page = session.Page;
        await Ready(page);
        await HeldReplies.HoldAsync(page);

        await page.ClickAsync("#save");
        var disabledAtPress = await page.Locator("#save").IsDisabledAsync();
        var readonlyAtPress = await page.Locator("#name").GetAttributeAsync("readonly");
        // A disabled button cannot be pressed at all; a script's requestSubmit still raises the event.
        await page.Locator("form").EvaluateAsync("f => f.requestSubmit()");
        var sentWhileHeld = await SubmitsAsync(page);
        await HeldReplies.ReleaseAsync(page);

        Assert.True(disabledAtPress, "the submit button was still live at the press.");
        Assert.NotNull(readonlyAtPress);
        Assert.Equal(1, sentWhileHeld);
        await Expect(page.Locator("#saves")).ToHaveTextAsync("saves=1");
        await Expect(page.Locator("#save")).ToBeEnabledAsync();
        await Expect(page.Locator("#name")).Not.ToHaveAttributeAsync("readonly", "");
        Assert.Equal(1, await SubmitsAsync(page));
    }

    [Fact]
    public async Task Enter_pressed_twice_in_a_field_submits_once()
    {
        await using var session = await HookSession.OpenAsync<InFlightFormPage>(playwright, beforeLoad: CountSubmits);
        var page = session.Page;
        await Ready(page);
        await page.FocusAsync("#name");
        await HeldReplies.HoldAsync(page);

        await page.Keyboard.PressAsync("Enter");
        await page.Keyboard.PressAsync("Enter");
        var sentWhileHeld = await SubmitsAsync(page);
        await HeldReplies.ReleaseAsync(page);

        Assert.Equal(1, sentWhileHeld);
        await Expect(page.Locator("#saves")).ToHaveTextAsync("saves=1");
        await Expect(page.Locator("#save")).ToBeEnabledAsync();
        await page.Keyboard.PressAsync("Enter");
        await Expect(page.Locator("#saves")).ToHaveTextAsync("saves=2");
    }

    // Holds the replies, and counts every submit the page sends, whichever frame carries it.
    private static async Task CountSubmits(IPage page)
    {
        await HeldReplies.Install(page);
        await page.AddInitScriptAsync(
            """
            window.__submits = 0;
            const send = WebSocket.prototype.send;
            WebSocket.prototype.send = function (data) {
                if (typeof data === "string") window.__submits += data.split('"type":"submit"').length - 1;
                return send.call(this, data);
            };
            """);
    }

    private static Task<int> SubmitsAsync(IPage page) => page.EvaluateAsync<int>("() => window.__submits");

    // The page is live once a press reaches the server and comes back.
    private static async Task Ready(IPage page)
    {
        await page.ClickAsync("#ready");
        await Expect(page.Locator("#is-ready")).ToHaveTextAsync("ready");
    }
}

/// <summary>A save that is not to run twice, and a field to press Enter in.</summary>
public sealed partial class InFlightFormPage : Component
{
    private readonly InFlightModel _model = new();
    private bool _ready;
    private int _saves;

    protected override Component? HeadAssets => Markup.Title["submit"];
    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        Button.Id("ready").OnClick(() => _ready = true)["ready"],
        P.Id("is-ready")[_ready ? "ready" : "not yet"],
        P.Id("saves")[$"saves={_saves}"],
        Form.Model(_model).OnSubmit(Save)[
            Input.Bind(() => _model.Name).Id("name"),
            Ui.Button.Primary.Submit.Id("save")["Save"]
        ]
    ];

    private async Task Save(InFlightModel model)
    {
        await Task.Delay(50);
        _saves++;
    }
}

internal sealed class InFlightModel
{
    public string Name { get; set; } = "";
}
