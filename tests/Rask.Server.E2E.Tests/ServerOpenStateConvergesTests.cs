using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     Every overlay whose open state the SERVER owns — a state-driven <c>Ui.Modal</c>, a controlled
///     <c>Ui.Dropdown</c>, the toasts — ends up showing what the server last said, even when the browser did not carry
///     out the one change the runtime was handed (<see cref="BrowserRefuses" />), on a slow processor and a slow line.
/// </summary>
/// <remarks>
///     Found as a modal the server had closed staying open in production (eszaf, 2026-11-11): the runtime acted on
///     the attribute's change alone, so a change lost once was lost for good. The runtime now sweeps the page back to
///     the server's word once the page has its answers (rask-owned.ts, <c>converges</c>).
/// </remarks>
public sealed class ServerOpenStateConvergesTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Modal = "dialog[data-rask-modal-open]";

    private const string Menu = "#menu [data-rask-popover-open]";

    private const string Toast = "[data-ui-toast-dialog]";

    [Theory]
    [InlineData("modal-close", 1280)]
    [InlineData("modal-close", 390)]
    [InlineData("modal-open", 1280)]
    [InlineData("modal-open", 390)]
    [InlineData("menu-hide", 1280)]
    [InlineData("menu-hide", 390)]
    [InlineData("menu-show", 1280)]
    [InlineData("menu-show", 390)]
    [InlineData("toast-show", 1280)]
    [InlineData("toast-show", 390)]
    public async Task What_the_server_says_last_is_what_the_browser_shows_when_the_browser_once_did_not_carry_it_out(string change, int width)
    {
        await using var session = await HookSession.OpenAsync<ServerOpenStatePage>(
            playwright, new() { ViewportSize = new() { Width = width, Height = 800 } });
        var page = session.Page;
        await session.HooksLoadedAsync();
        await SlowAsync(page);
        await BrowserRefuses.InstallAsync(page);

        for (var run = 0; run < 5; run++)
        {
            await ChangeAsync(page, change);
        }

        Assert.Empty(session.PageErrors);
    }

    private static async Task ChangeAsync(IPage page, string change)
    {
        switch (change)
        {
            case "modal-close":
                await page.ClickAsync("#open-modal");
                await Expect(page.Locator(Modal)).ToBeVisibleAsync();
                await BrowserRefuses.NextAsync(page, "close");
                await page.ClickAsync("#close-modal");
                await Expect(page.Locator(Modal)).ToHaveAttributeAsync("data-rask-modal-open", "false");
                await Expect(page.Locator(Modal)).ToBeHiddenAsync();
                await Expect(page.Locator("#modal-closes")).Not.ToHaveTextAsync("0");
                break;
            case "modal-open":
                await BrowserRefuses.NextAsync(page, "showModal");
                await page.ClickAsync("#open-modal");
                await Expect(page.Locator(Modal)).ToBeVisibleAsync();
                Assert.True(await page.Locator(Modal).EvaluateAsync<bool>("d => d.matches(':modal')"), "the dialog opened, but not as a modal");
                await page.ClickAsync("#close-modal");
                await Expect(page.Locator(Modal)).ToBeHiddenAsync();
                break;
            case "menu-hide":
                // Opened, then closed by the server a moment later in the same handler: nothing outside it is pressed.
                await BrowserRefuses.NextAsync(page, "hidePopover");
                await page.ClickAsync("#menu-briefly");
                await Expect(page.Locator(Menu)).ToBeVisibleAsync();
                await Expect(page.Locator(Menu)).ToHaveAttributeAsync("data-rask-popover-open", "false");
                await Expect(page.Locator(Menu)).ToBeHiddenAsync();
                break;
            case "menu-show":
                await BrowserRefuses.NextAsync(page, "showPopover");
                await page.ClickAsync("#open-menu");
                await Expect(page.Locator(Menu)).ToBeVisibleAsync();
                await page.Keyboard.PressAsync("Escape");
                await Expect(page.Locator(Menu)).ToBeHiddenAsync();
                await Expect(page.Locator(Menu)).ToHaveAttributeAsync("data-rask-popover-open", "false");
                break;
            default:
                await BrowserRefuses.NextAsync(page, "showPopover");
                await page.ClickAsync("#toast");
                await Expect(page.Locator(Toast).Last).ToBeVisibleAsync();
                break;
        }
    }

    // A quarter of a processor and a line with a round trip of a quarter second.
    private static async Task SlowAsync(IPage page)
    {
        var cdp = await page.Context.NewCDPSessionAsync(page);
        await cdp.SendAsync("Emulation.setCPUThrottlingRate", new Dictionary<string, object> { ["rate"] = 4 });
        await cdp.SendAsync("Network.enable");
        await cdp.SendAsync("Network.emulateNetworkConditions", new Dictionary<string, object>
        {
            ["offline"] = false, ["latency"] = 120, ["downloadThroughput"] = -1, ["uploadThroughput"] = -1,
        });
    }
}

/// <summary>A modal, a controlled dropdown and the toasts, each opened and closed by the server.</summary>
public sealed partial class ServerOpenStatePage : Component
{
    private bool _modal;
    private bool _menu;
    private int _closes;

    protected override Component? HeadAssets => Markup.Title["server open state"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() => Div[
        Main.Class("space-y-4 p-4")[
            P.Id("modal-closes")[$"{_closes}"],
            Ui.Button.Id("open-modal").OnClick(() => { _modal = true; })["Open the modal"],
            Ui.Modal.Key("server-modal").Open(_modal).OnClose(() => { _modal = false; })[
                Ui.Heading["Server modal"],
                Ui.Button.Id("close-modal").OnClick(CloseModalLaterAsync)["Close"]
            ],
            Ui.Button.Id("open-menu").OnClick(() => { _menu = true; })["Open the menu"],
            Ui.Button.Id("menu-briefly").OnClick(MenuBriefly)["Show the menu briefly"],
            Div.Id("menu")[
                Ui.Dropdown.Open(_menu).OnToggle(open => { _menu = open; })[
                    Ui.Button["Options"],
                    Ui.Menu[Ui.MenuItem["Edit"], Ui.MenuItem["Copy"]]
                ]
            ],
            Ui.Button.Id("toast").OnClick(() => Toast.Success("Saved."))["Toast"]
        ],
        Ui.Toast
    ];

    // A job that takes a moment, then closes the modal: what the reported dialog's submit did.
    private async Task CloseModalLaterAsync()
    {
        await Task.Delay(300);
        _modal = false;
        _closes++;
    }

    // Open now; closed by the server a moment later, in a render no event of the page asked for.
    private void MenuBriefly()
    {
        _menu = true;
        _ = CloseMenuLaterAsync();
    }

    private async Task CloseMenuLaterAsync()
    {
        await Task.Delay(600);
        _menu = false;
        StateHasChanged();
    }
}
