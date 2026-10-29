using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     Escape with a toast showing, as on Flux (measured on fluxui.dev 2026-10-29): it takes the toast down, and a
///     modal under the toast closes on the same press — with focus on the page, on a button in the modal or in a
///     field of it.
/// </summary>
public sealed class ToastEscapeTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Toast = "[data-ui-toast-dialog]";

    [Fact]
    public async Task Escape_takes_a_toast_on_its_own_down()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.ClickAsync("#raise");
        await Expect(page.Locator(Toast)).ToBeVisibleAsync();

        await page.Keyboard.PressAsync("Escape");

        await Expect(page.Locator(Toast)).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData("#save")]
    [InlineData("#title")]
    public async Task One_Escape_closes_the_toast_and_the_modal_under_it(string focused)
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.ClickAsync("#open");
        await Expect(page.Locator("#edit")).ToBeVisibleAsync();
        await page.ClickAsync("#save");
        await Expect(page.Locator(Toast)).ToBeVisibleAsync();
        await page.FocusAsync(focused);

        await page.Keyboard.PressAsync("Escape");

        await Expect(page.Locator(Toast)).ToHaveCountAsync(0);
        await Expect(page.Locator("#edit")).ToBeHiddenAsync();
        await Expect(page.Locator("#closed")).ToHaveTextAsync("closed=1");
    }

    private async Task<HookSession> OpenAsync()
    {
        var session = await HookSession.OpenAsync<ToastOverModalPage>(playwright);
        await session.HooksLoadedAsync();
        return session;
    }
}

/// <summary>A modal whose save raises a toast that stays until dismissed, with the layout's toast host.</summary>
public sealed partial class ToastOverModalPage : Component
{
    private bool _open;
    private int _closed;
    private readonly ConfirmOrder _draft = new();

    protected override Component? HeadAssets => Markup.Title["toast over a modal"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("closed")[$"closed={_closed}"],
        Button.Id("open").OnClick(() => _open = true)["open"],
        Button.Id("raise").OnClick(Raise)["raise"],
        Ui.Modal.Name("edit").Open(_open).OnClose(Closed)[
            Input.Bind(() => _draft.Item).Id("title"),
            Button.Id("save").OnClick(Raise)["save"]
        ],
        Ui.Toast
    ];

    private static void Raise() => Rask.Core.Toast.Info("Saved.").UntilDismissed();

    private void Closed()
    {
        _open = false;
        _closed++;
    }
}
