using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A password the reader typed stays in their browser: the server hears it with the next action and never
///     sends it back, though it changes the model, and a clear still empties the field. Every frame the page
///     receives is read.
/// </summary>
public sealed class PasswordNeverEchoedTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Typed = "typed-secret-1";
    private const string Chosen = "server-chosen-2";

    [Fact]
    public async Task A_password_the_server_changes_is_in_no_frame_and_the_field_keeps_what_was_typed()
    {
        await using var session = await HookSession.OpenAsync<PasswordPage>(playwright, beforeLoad: TapFrames);
        var page = session.Page;
        await page.ClickAsync("#ready");
        await Expect(page.Locator("#is-ready")).ToHaveTextAsync("ready");

        await page.Locator("#pw").PressSequentiallyAsync(Typed);
        await page.ClickAsync("#set");
        await Expect(page.Locator("#len")).ToHaveTextAsync($"len={Chosen.Length}");
        var kept = await page.Locator("#pw").InputValueAsync();
        var received = await ReceivedAsync(page);

        Assert.Equal(Typed, kept);
        Assert.DoesNotContain(Typed, received, StringComparison.Ordinal);
        Assert.DoesNotContain(Chosen, received, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_password_the_server_clears_empties_the_field()
    {
        await using var session = await HookSession.OpenAsync<PasswordPage>(playwright, beforeLoad: TapFrames);
        var page = session.Page;
        await page.ClickAsync("#ready");
        await Expect(page.Locator("#is-ready")).ToHaveTextAsync("ready");
        await page.Locator("#pw").PressSequentiallyAsync(Typed);
        await page.ClickAsync("#set");
        await Expect(page.Locator("#len")).ToHaveTextAsync($"len={Chosen.Length}");

        await page.ClickAsync("#clear");

        await Expect(page.Locator("#pw")).ToHaveValueAsync("");
        Assert.DoesNotContain(Typed, await ReceivedAsync(page), StringComparison.Ordinal);
    }

    private static Task TapFrames(IPage page) => page.AddInitScriptAsync(
        """
        window.__in = [];
        const Native = window.WebSocket;
        window.WebSocket = class extends Native {
            constructor(...args) {
                super(...args);
                this.addEventListener("message", e => window.__in.push(String(e.data)));
            }
        };
        """);

    private static async Task<string> ReceivedAsync(IPage page) =>
        string.Join('\n', await page.EvaluateAsync<string[]>("() => window.__in"));
}

public sealed partial class PasswordPage : Component
{
    private readonly Model _m = new();
    private bool _ready;

    protected override Component? HeadAssets => Markup.Title["password"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("is-ready")[_ready ? "ready" : "loading"],
        P.Id("len")[$"len={_m.Password.Length}"],
        Button.Id("ready").OnClick(() => _ready = true)["ready"],
        Button.Id("set").OnClick(() => _m.Password = "server-chosen-2")["set"],
        Button.Id("clear").OnClick(() => _m.Password = "")["clear"],
        Form.Model(_m)[Input.Bind(() => _m.Password).Type(InputType.Password).Id("pw")]
    ];

    private sealed class Model
    {
        public string Password { get; set; } = "";
    }
}
