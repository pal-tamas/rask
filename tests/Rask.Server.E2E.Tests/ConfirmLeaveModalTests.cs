using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A guarded form inside a <c>Ui.Modal</c> in a real browser: closing the modal is leaving the form, so every
///     way the reader closes it asks first in <c>Ui.ConfirmLeave</c>, staying keeps the modal and what was typed,
///     and letting it go closes it once — the page hears the close — and asks nothing more.
/// </summary>
public sealed class ConfirmLeaveModalTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Leave = "#ui-confirm-leave";

    private const string Edit = "#edit";

    [Theory]
    [InlineData("x")]
    [InlineData("escape")]
    [InlineData("outside")]
    [InlineData("cancel")]
    public async Task Every_way_the_reader_closes_the_modal_asks_and_staying_keeps_it_and_what_was_typed(string way)
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.FillAsync("#title", "Minutes");

        await CloseAsync(page, way);
        await Expect(page.Locator(Leave)).ToBeVisibleAsync();
        await Expect(page.Locator(Leave + " h2")).ToHaveTextAsync("Discard the minutes?");
        await page.ClickAsync(Leave + " .flex button:not([data-rask-leave])");
        await Expect(page.Locator(Leave)).ToBeHiddenAsync();

        Assert.True(await page.Locator(Edit).EvaluateAsync<bool>("d => d.matches(':modal')"));
        await Expect(page.Locator("#title")).ToHaveValueAsync("Minutes");
        await Expect(page.Locator("#closed")).ToHaveTextAsync("closed=0");
        Assert.Empty(session.BrowserDialogs);
    }

    [Theory]
    [InlineData("x")]
    [InlineData("escape")]
    [InlineData("outside")]
    [InlineData("cancel")]
    public async Task Letting_the_modal_go_closes_it_once_and_the_page_hears_it(string way)
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.FillAsync("#title", "Minutes");

        await CloseAsync(page, way);
        await page.ClickAsync(Leave + " [data-rask-leave=go]");

        await Expect(page.Locator(Edit)).ToBeHiddenAsync();
        await Expect(page.Locator("#closed")).ToHaveTextAsync("closed=1");
        await page.ClickAsync("#to-other");
        await Expect(page.Locator("#path")).ToHaveTextAsync("path=/other");
        Assert.Empty(session.BrowserDialogs);
    }

    [Fact]
    public async Task A_modal_nobody_typed_into_closes_on_Escape_unasked()
    {
        await using var session = await OpenAsync();
        var page = session.Page;

        await page.FocusAsync("#title");
        await page.Keyboard.PressAsync("Escape");

        await Expect(page.Locator(Edit)).ToBeHiddenAsync();
        await Expect(page.Locator(Leave)).ToBeHiddenAsync();
        await Expect(page.Locator("#closed")).ToHaveTextAsync("closed=1");
        Assert.Empty(session.BrowserDialogs);
    }

    [Fact]
    public async Task A_save_that_closes_the_modal_from_the_page_is_never_asked_about()
    {
        await using var session = await OpenAsync();
        var page = session.Page;

        await page.FillAsync("#title", "Minutes");
        await page.ClickAsync("#save");

        await Expect(page.Locator("#saved")).ToHaveTextAsync("saved=Minutes");
        await Expect(page.Locator(Edit)).ToBeHiddenAsync();
        await Expect(page.Locator(Leave)).ToBeHiddenAsync();
        Assert.Empty(session.BrowserDialogs);
    }

    private static async Task CloseAsync(IPage page, string way)
    {
        switch (way)
        {
            case "x":
                await page.ClickAsync(Edit + " button[aria-label='Close modal']");
                break;
            case "escape":
                await page.FocusAsync("#title");
                await page.Keyboard.PressAsync("Escape");
                break;
            case "cancel":
                await page.ClickAsync("#cancel");
                break;
            default:
                // The backdrop: a corner of the viewport, well outside the centred panel.
                await page.Mouse.ClickAsync(5, 690);
                break;
        }
    }

    private async Task<ModalSession> OpenAsync()
    {
        var session = await HookSession.OpenAsync<LeaveModalPage>(playwright);
        List<string> browserDialogs = [];
        session.Page.Dialog += (_, dialog) =>
        {
            browserDialogs.Add(dialog.Type + ":" + dialog.Message);
            _ = dialog.DismissAsync();
        };
        await session.HooksLoadedAsync();
        await session.Page.ClickAsync("#open");
        await Expect(session.Page.Locator(Edit)).ToBeVisibleAsync();
        return new ModalSession(session, browserDialogs);
    }

    private sealed record ModalSession(HookSession Session, List<string> BrowserDialogs) : IAsyncDisposable
    {
        public IPage Page => Session.Page;

        public ValueTask DisposeAsync() => Session.DisposeAsync();
    }
}

/// <summary>A guarded form in a modal the page opens and closes, with the kit's dialog placed beside it as a layout would.</summary>
public sealed partial class LeaveModalPage(RouteState route) : Component
{
    private Minutes _minutes = new();
    private bool _open;
    private string _saved = "";
    private int _closed;

    protected override Component? HeadAssets => Markup.Title["leave modal"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("path")[$"path={route.Path}"],
        P.Id("saved")[$"saved={_saved}"],
        P.Id("closed")[$"closed={_closed}"],
        Button.Id("open").OnClick(Open)["open"],
        NavLink.Href("/other").Id("to-other")["other"],
        Ui.Modal.Name("edit").Open(_open).OnClose(Closed)[
            _open
                ? Form.Model(_minutes).OnSubmit(Save).NoValidate(true).ConfirmLeave("Discard the minutes?")[
                    Input.Bind(() => _minutes.Title).Id("title"),
                    Button.Type(ButtonType.Submit).Id("save")["save"],
                    Ui.ModalClose[Button.Type(ButtonType.Button).Id("cancel")["cancel"]]
                ]
                : null
        ],
        Ui.ConfirmLeave.Stay("Nem").Leave("Igen")
    ];

    private void Open()
    {
        _minutes = new Minutes();
        _open = true;
    }

    private void Closed()
    {
        if (_open)
        {
            _open = false;
            _closed++;
        }
    }

    private void Save(Minutes minutes)
    {
        _saved = minutes.Title;
        _open = false;
    }
}
