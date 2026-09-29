using Rask.Core;
using Rask.Core.Components;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a one-element test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A ref typed to its element's MDN interface calls that element's own members from a server handler, over the
///     socket, and reads its live state back.
/// </summary>
public sealed class TypedElementRefTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Fact]
    public async Task A_server_handler_opens_a_dialog_as_a_modal_and_reads_its_open_state_back()
    {
        await using var host = await LiveServerHost.StartAsync<DialogRefPage>(blockWebSockets: false);
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(host.BaseUrl + "/");
        await Expect(page.Locator("#state")).ToHaveTextAsync("open=?", new() { Timeout = 15_000 });

        await page.Locator("#open").ClickAsync();

        await Expect(page.Locator("#state")).ToHaveTextAsync("open=True", new() { Timeout = 15_000 });
        Assert.True(await page.Locator("#dlg").EvaluateAsync<bool>("d => d.matches(':modal')"), "showModal() did not open a modal");

        await page.Locator("#close").ClickAsync();

        await Expect(page.Locator("#state")).ToHaveTextAsync("open=False", new() { Timeout = 15_000 });
    }
}

public sealed partial class DialogRefPage : Component
{
    private readonly ElementRef<HTMLDialogElement> _dialog = new();
    private string _state = "?";

    protected override Component? HeadAssets => Markup.Title["dialog ref"];
    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("state")[$"open={_state}"],
        Button.Id("open").OnClick(async () =>
        {
            await _dialog.ShowModal();
            _state = (await _dialog.Open).ToString();
        })["open"],
        Dialog.Id("dlg").Ref(_dialog)[
            Button.Id("close").OnClick(async () =>
            {
                await _dialog.Close();
                _state = (await _dialog.Open).ToString();
            })["close"]
        ]
    ];
}
