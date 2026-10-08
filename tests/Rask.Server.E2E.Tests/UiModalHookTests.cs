using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     <c>Ui.Modal</c> on a live SERVER page: what the component writes, the runtime's hooks act on, and the
///     page's own handlers hear over the socket.
/// </summary>
/// <remarks>
///     <c>RuntimeHookOverlayTests</c> pins the hooks against attributes written by hand, and the site's
///     <c>UiKitActionsTests</c> drives the same component in WebAssembly. This is the seam between them on the
///     other host: a render that says <c>data-rask-modal-open</c>, and a <c>close</c> the browser raised
///     arriving at <c>OnClose</c> a round trip later, while the dialog is already shut.
/// </remarks>
public sealed class UiModalHookTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Fact]
    public async Task A_modal_the_pages_state_opens_is_a_real_modal_and_the_page_hears_the_reader_close_it()
    {
        await using var session = await HookSession.OpenAsync<ModalHookPage>(playwright);
        var page = session.Page;
        var dialog = page.Locator("[data-testid='state'] dialog");

        await page.ClickAsync("#open");
        await Expect(dialog).ToBeVisibleAsync();
        var modal = await dialog.EvaluateAsync<bool>("d => d.matches(':modal')");
        var marked = await dialog.EvaluateAsync<bool>("d => d.hasAttribute('data-open')");
        var locked = await page.EvaluateAsync<string>("() => getComputedStyle(document.documentElement).overflowY");
        await page.Keyboard.PressAsync("Escape");

        Assert.True(modal, "the page's state opened a dialog that is not modal");
        Assert.True(marked);
        Assert.Equal("hidden", locked);
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(page.Locator("#heard")).ToHaveTextAsync("cancel close");
        await Expect(page.Locator("#state")).ToHaveTextAsync("closed");
        await Expect(dialog).ToHaveAttributeAsync("data-rask-modal-open", "false");
    }

    [Fact]
    public async Task The_page_closes_its_modal_from_a_handler_and_opens_it_again()
    {
        await using var session = await HookSession.OpenAsync<ModalHookPage>(playwright);
        var page = session.Page;
        var dialog = page.Locator("[data-testid='state'] dialog");

        await page.ClickAsync("#open");
        await Expect(dialog).ToBeVisibleAsync();
        await page.ClickAsync("#done");
        await Expect(dialog).ToBeHiddenAsync();
        // A close, not a dismissal: the handler changed the page's mind and nothing was cancelled.
        await Expect(page.Locator("#heard")).ToHaveTextAsync("close");
        await page.ClickAsync("#open");

        await Expect(dialog).ToBeVisibleAsync();
        Assert.True(await dialog.EvaluateAsync<bool>("d => d.matches(':modal')"));
    }

    [Fact]
    public async Task A_modal_close_and_the_corner_button_close_a_state_driven_modal_and_the_page_catches_up()
    {
        await using var session = await HookSession.OpenAsync<ModalHookPage>(playwright);
        var page = session.Page;
        var dialog = page.Locator("[data-testid='state'] dialog");

        await page.ClickAsync("#open");
        await Expect(dialog).ToBeVisibleAsync();
        await page.ClickAsync("#cancel");
        await Expect(page.Locator("#state")).ToHaveTextAsync("closed");
        await page.ClickAsync("#open");
        await Expect(dialog).ToBeVisibleAsync();
        await dialog.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Close modal" }).ClickAsync();

        await Expect(dialog).ToBeHiddenAsync();
        await Expect(page.Locator("#state")).ToHaveTextAsync("closed");
        await Expect(page.Locator("#heard")).ToHaveTextAsync("close");
    }

    [Fact]
    public async Task A_named_modal_that_is_not_escapable_stays_through_Escape_and_a_press_outside_dismisses_it()
    {
        await using var session = await HookSession.OpenAsync<ModalHookPage>(playwright);
        var page = session.Page;
        var dialog = page.Locator("#session");

        await page.ClickAsync("#session-open");
        await Expect(dialog).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");
        await page.Keyboard.PressAsync("Escape");
        var afterEscape = await dialog.EvaluateAsync<bool>("d => d.open");
        await page.Mouse.ClickAsync(5, 690);

        Assert.True(afterEscape, "Escape closed a modal that is not escapable");
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(page.Locator("#heard")).ToHaveTextAsync("cancel close");
        await Expect(page.Locator("#session-open")).ToBeFocusedAsync();
    }
}

/// <summary>One modal the page's state opens and one a trigger opens, each reporting what it heard.</summary>
public sealed partial class ModalHookPage : Component
{
    private readonly List<string> _heard = [];
    private bool _open;

    protected override Component? HeadAssets => Markup.Title["modal hooks"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("heard")[string.Join(' ', _heard)],
        P.Id("state")[_open ? "open" : "closed"],
        Button.Id("open").OnClick(() => { _heard.Clear(); _open = true; })["open"],
        Div.Data("testid", "state")[
            Ui.Modal
                .Open(_open)
                .OnCancel(() => _heard.Add("cancel"))
                .OnClose(() => { _heard.Add("close"); _open = false; })[
                Button.Id("done").OnClick(() => { _open = false; })["done"],
                Ui.ModalClose[Button.Id("cancel")["cancel"]]
            ]
        ],
        Ui.ModalTrigger.Name("session")[Button.Id("session-open").OnClick(() => _heard.Clear())["session"]],
        Ui.Modal
            .Name("session")
            .Escapable(false)
            .OnCancel(() => _heard.Add("cancel"))
            .OnClose(() => _heard.Add("close"))[
            P["Escape does not close this."]
        ]
    ];
}
