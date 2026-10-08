using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     <c>Ui.ConfirmLeave</c> in a real browser: with it on the page, a guarded form asks in that dialog and not
///     in the browser's <c>confirm</c>, every way of closing it but one keeps the reader where they were, and
///     that one carries on with the navigation that was asked for.
/// </summary>
/// <remarks>
///     The page is <see cref="LeaveGuardPage" /> with the element added, so <c>ConfirmLeaveHookTests</c> beside
///     this is the same journeys without it. Every test fails on a browser dialog: with the element there, one
///     appearing is the bug.
/// </remarks>
public sealed class ConfirmLeaveDialogTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Dialog = "#ui-confirm-leave";

    [Fact]
    public async Task A_press_on_a_nav_link_opens_the_dialog_with_the_forms_own_message_and_sends_nothing()
    {
        await using var session = await OpenAsync();
        var page = session.Page;

        await page.FillAsync("#title", "Minutes");
        await page.ClickAsync("#to-list");

        await Expect(page.Locator(Dialog)).ToBeVisibleAsync();
        await Expect(page.Locator(Dialog + " h2")).ToHaveTextAsync("Leave without saving?");
        Assert.True(await page.Locator(Dialog).EvaluateAsync<bool>("d => d.matches(':modal') && d.contains(document.activeElement)"));
        await Expect(page.Locator("#path")).ToHaveTextAsync("path=/");
        Assert.Empty(session.BrowserDialogs);
    }

    [Theory]
    [InlineData("stay")]
    [InlineData("x")]
    [InlineData("escape")]
    [InlineData("outside")]
    public async Task Every_way_of_closing_the_dialog_but_Leave_keeps_the_page_and_what_was_typed(string way)
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.FillAsync("#title", "Minutes");
        await page.ClickAsync("#to-list");
        await Expect(page.Locator(Dialog)).ToBeVisibleAsync();

        await CloseAsync(page, way);
        await Expect(page.Locator(Dialog)).ToBeHiddenAsync();
        await page.ClickAsync("#count");

        await Expect(page.Locator("#counted")).ToHaveTextAsync("count=1");
        await Expect(page.Locator("#path")).ToHaveTextAsync("path=/");
        await Expect(page.Locator("#title")).ToHaveValueAsync("Minutes");
        Assert.Equal("/", new Uri(page.Url).AbsolutePath);
        Assert.Empty(session.BrowserDialogs);
    }

    [Fact]
    public async Task Leave_carries_on_to_the_page_the_link_led_to_and_the_form_that_left_guards_nothing()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.FillAsync("#title", "Minutes");
        await page.ClickAsync("#to-list");

        await page.ClickAsync(Dialog + " [data-rask-leave=go]");
        await Expect(page.Locator("#path")).ToHaveTextAsync("path=/list");
        await page.ClickAsync("#to-other");

        await Expect(page.Locator("#path")).ToHaveTextAsync("path=/other");
        await Expect(page.Locator(Dialog)).ToBeHiddenAsync();
        Assert.Equal("/other", new Uri(page.Url).AbsolutePath);
        Assert.Empty(session.BrowserDialogs);
    }

    [Fact]
    public async Task A_navigation_from_front_end_code_waits_for_the_dialog_and_Leave_makes_it()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.FillAsync("#title", "Minutes");

        await page.EvaluateAsync("() => window.__raskHost.navigate('/list?from=code')");
        await Expect(page.Locator(Dialog)).ToBeVisibleAsync();
        var pathWhileAsked = await page.TextContentAsync("#path");
        await page.ClickAsync(Dialog + " [data-rask-leave=go]");

        await Expect(page.Locator("#path")).ToHaveTextAsync("path=/list");
        Assert.Equal("path=/", pathWhileAsked);
        Assert.EndsWith("/list?from=code", page.Url, StringComparison.Ordinal);
        Assert.Empty(session.BrowserDialogs);
    }

    [Fact]
    public async Task Staying_after_Back_leaves_the_address_and_the_history_where_they_were_and_Leave_goes_back()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.ClickAsync("#to-list");
        await Expect(page.Locator("#path")).ToHaveTextAsync("path=/list");
        await page.ClickAsync("#to-form");
        await page.FillAsync("#title", "Minutes");

        await page.EvaluateAsync("() => history.back()");
        await Expect(page.Locator(Dialog)).ToBeVisibleAsync();
        await CloseAsync(page, "stay");
        await page.ClickAsync("#count");
        await Expect(page.Locator("#counted")).ToHaveTextAsync("count=1");
        var addressAfterStaying = new Uri(page.Url).AbsolutePath;
        var pathAfterStaying = await page.TextContentAsync("#path");
        var typedAfterStaying = await page.InputValueAsync("#title");
        await page.EvaluateAsync("() => history.back()");
        await Expect(page.Locator(Dialog)).ToBeVisibleAsync();
        await page.ClickAsync(Dialog + " [data-rask-leave=go]");

        // One entry back from where the reader stayed is the list: the refused Back moved nothing.
        await Expect(page.Locator("#path")).ToHaveTextAsync("path=/list");
        Assert.Equal("/", addressAfterStaying);
        Assert.Equal("path=/", pathAfterStaying);
        Assert.Equal("Minutes", typedAfterStaying);
        Assert.Equal("/list", new Uri(page.Url).AbsolutePath);
        Assert.Empty(session.BrowserDialogs);
    }

    [Fact]
    public async Task A_form_nobody_typed_into_and_a_save_that_goes_elsewhere_never_open_the_dialog()
    {
        await using var session = await OpenAsync();
        var page = session.Page;

        await page.ClickAsync("#to-list");
        await Expect(page.Locator("#path")).ToHaveTextAsync("path=/list");
        await page.ClickAsync("#to-form");
        await page.FillAsync("#title", "go");
        await page.ClickAsync("#save");

        await Expect(page.Locator("#saved")).ToHaveTextAsync("saved=go");
        await Expect(page.Locator("#path")).ToHaveTextAsync("path=/list");
        await Expect(page.Locator(Dialog)).ToBeHiddenAsync();
        Assert.Empty(session.BrowserDialogs);
    }

    [Fact]
    public async Task The_dialog_carries_the_labels_the_layout_gave_it_and_focus_returns_to_the_page_when_it_closes()
    {
        await using var session = await OpenAsync();
        var page = session.Page;
        await page.FillAsync("#title", "Minutes");
        await page.FocusAsync("#to-list");

        await page.Keyboard.PressAsync("Enter");
        await Expect(page.Locator(Dialog)).ToBeVisibleAsync();
        var labels = await page.Locator(Dialog + " .flex button").AllInnerTextsAsync();
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator(Dialog)).ToBeHiddenAsync();

        Assert.Equal(["Nem", "Igen"], labels.Select(label => label.Trim()));
        Assert.Equal("to-list", await page.EvaluateAsync<string>("() => document.activeElement.id"));
    }

    private static async Task CloseAsync(IPage page, string way)
    {
        switch (way)
        {
            case "stay":
                await page.ClickAsync(Dialog + " .flex button:not([data-rask-leave])");
                break;
            case "x":
                await page.ClickAsync(Dialog + " button[aria-label='Close modal']");
                break;
            case "escape":
                await page.Keyboard.PressAsync("Escape");
                break;
            default:
                // The backdrop: a corner of the viewport, well outside the centred panel.
                await page.Mouse.ClickAsync(5, 690);
                break;
        }
    }

    private async Task<DialogSession> OpenAsync()
    {
        var session = await HookSession.OpenAsync<LeaveDialogPage>(playwright);
        List<string> browserDialogs = [];
        session.Page.Dialog += (_, dialog) =>
        {
            browserDialogs.Add(dialog.Type + ":" + dialog.Message);
            _ = dialog.DismissAsync();
        };
        await session.HooksLoadedAsync();
        return new DialogSession(session, browserDialogs);
    }

    private sealed record DialogSession(HookSession Session, List<string> BrowserDialogs) : IAsyncDisposable
    {
        public IPage Page => Session.Page;

        public ValueTask DisposeAsync() => Session.DisposeAsync();
    }
}

/// <summary><see cref="LeaveGuardPage" />'s form and links, with the kit's dialog placed beside them as a layout would.</summary>
public sealed partial class LeaveDialogPage(RouteState route) : Component
{
    private readonly Minutes _minutes = new();
    private string _saved = "";
    private int _count;

    protected override Component? HeadAssets => Markup.Title["leave dialog"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("path")[$"path={route.Path}"],
        P.Id("saved")[$"saved={_saved}"],
        P.Id("counted")[$"count={_count}"],
        Button.Id("count").OnClick(() => _count++)["count"],
        NavLink.Href("/").Id("to-form")["form"],
        NavLink.Href("/list").Id("to-list")["list"],
        NavLink.Href("/other").Id("to-other")["other"],
        string.Equals(route.Path, "/", StringComparison.Ordinal)
            ? Form.Model(_minutes).OnSubmit(Save).NoValidate(true).ConfirmLeave("Leave without saving?")[
                Input.Bind(() => _minutes.Title).Id("title"),
                Button.Type(ButtonType.Submit).Id("save")["save"]
            ]
            : null,
        Ui.ConfirmLeave.Stay("Nem").Leave("Igen")
    ];

    private void Save(Minutes minutes)
    {
        _saved = minutes.Title;
        if (string.Equals(minutes.Title, "go", StringComparison.Ordinal))
        {
            Go.To("/list");
        }
    }
}
