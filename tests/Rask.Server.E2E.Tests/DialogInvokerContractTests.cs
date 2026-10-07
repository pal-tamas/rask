using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Server.E2E.Tests;

/// <summary>
///     The platform behaviour Rask UI's modal is built on, pinned in a real browser.
/// </summary>
/// <remarks>
///     <para>
///     <c>Ui.ModalTrigger</c> names its dialog in an invoker command — <c>command="show-modal" commandfor</c> —
///     and the modal's own buttons close it with <c>command="close"</c> and, for the room around a panel whose
///     layer scrolls, <c>command="request-close"</c>. No handler runs for any of them, so three platform facts
///     hold the component up: the command opens a real modal (inert page, focus handed back), <c>close</c>
///     closes it without a <c>cancel</c>, and <c>request-close</c> raises <c>cancel</c> before <c>close</c>,
///     which is what tells <c>OnCancel</c> a dismissal from a close. A fourth holds up the focus placeholder:
///     an <c>autofocus</c> element inside takes the focus the dialog would otherwise hand its first control.
///     </para>
///     <para>
///     What the RUNTIME adds — Escape and a press outside per <c>data-rask-modal</c>, <c>data-open</c>, the
///     page lock, a dialog the page's state opens, and these commands where the engine has none — is pinned
///     by <c>RuntimeHookOverlayTests</c>.
///     </para>
///     <para>
///     The markup is the shape <c>UiModal</c> renders, written out rather than rendered, because this project does
///     not reference the kit: what is under test is the browser. The component itself is driven end to end by
///     <c>UiKitActionsTests</c> and <c>UiModalHookTests</c>.
///     </para>
/// </remarks>
public sealed class DialogInvokerContractTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Markup = """
        <!doctype html>
        <html lang="en"><body style="height:3000px">
          <button id="before">before</button>
          <button id="open" type="button" command="show-modal" commandfor="dlg">Open</button>
          <dialog id="dlg" data-modal="dlg" style="padding:24px">
            <div id="placeholder" tabindex="-1" data-ui-focus-placeholder autofocus></div>
            <button id="first" type="button">First</button>
            <button id="dismiss" type="button" command="request-close" commandfor="dlg">Dismiss</button>
            <button id="close" type="button" command="close" commandfor="dlg">Close</button>
          </dialog>
          <button id="after">after</button>
        </body></html>
        """;

    private const string Listen =
        "() => { window.heard = []; for (const type of ['cancel', 'close']) dlg.addEventListener(type, () => window.heard.push(type)); }";

    [Fact]
    public async Task The_invoker_command_opens_a_dialog_as_a_real_modal_and_hands_focus_back()
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.SetContentAsync(Markup);

        var dialog = page.Locator("#dlg");
        await page.Locator("#open").FocusAsync();
        await page.Keyboard.PressAsync("Enter");

        await Expect(dialog).ToBeVisibleAsync();
        Assert.True(await dialog.EvaluateAsync<bool>("d => d.matches(':modal')"), "the dialog opened, but not as a modal");

        // The page behind is inert: Tab from the last control inside wraps to the document, never to #after.
        for (var i = 0; i < 5; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            var focused = await page.EvaluateAsync<string?>("() => document.activeElement && document.activeElement.id");
            Assert.NotEqual("after", focused);
            Assert.NotEqual("before", focused);
        }

        // Escape closes it and focus goes back to the button that opened it.
        await page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToBeHiddenAsync();
        Assert.Equal("open", await page.EvaluateAsync<string?>("() => document.activeElement && document.activeElement.id"));
    }

    [Fact]
    public async Task The_close_command_closes_the_modal_without_a_cancel()
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.SetContentAsync(Markup);
        await page.EvaluateAsync(Listen);

        await page.ClickAsync("#open");
        await Expect(page.Locator("#dlg")).ToBeVisibleAsync();
        await page.ClickAsync("#close");

        await Expect(page.Locator("#dlg")).ToBeHiddenAsync();
        await page.WaitForFunctionAsync("() => window.heard.includes('close')");
        Assert.Equal("close", await page.EvaluateAsync<string>("() => window.heard.join(',')"));
    }

    [Fact]
    public async Task The_request_close_command_raises_cancel_before_close()
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.SetContentAsync(Markup);
        await page.EvaluateAsync(Listen);

        await page.ClickAsync("#open");
        await Expect(page.Locator("#dlg")).ToBeVisibleAsync();
        await page.ClickAsync("#dismiss");

        // Ui.Modal.OnCancel is this cancel and OnClose what follows it. The close event is queued behind the
        // closing itself, so it is waited for rather than read.
        await Expect(page.Locator("#dlg")).ToBeHiddenAsync();
        await page.WaitForFunctionAsync("() => window.heard.includes('close')");
        Assert.Equal("cancel,close", await page.EvaluateAsync<string>("() => window.heard.join(',')"));
    }

    [Fact]
    public async Task The_autofocus_placeholder_takes_the_focus_a_modal_would_give_its_first_control()
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.SetContentAsync(Markup);

        await page.Locator("#open").FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Expect(page.Locator("#dlg")).ToBeVisibleAsync();

        // Not #first: nothing is ringed when it opens. And out of the tab order, so the first Tab is #first.
        Assert.Equal("placeholder", await page.EvaluateAsync<string?>("() => document.activeElement && document.activeElement.id"));
        await page.Keyboard.PressAsync("Tab");
        Assert.Equal("first", await page.EvaluateAsync<string?>("() => document.activeElement && document.activeElement.id"));
    }
}
