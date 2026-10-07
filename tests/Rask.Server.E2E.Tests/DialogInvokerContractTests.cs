using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Server.E2E.Tests;

/// <summary>
///     The platform behaviour Rask UI's modal is built on, pinned in a real browser.
/// </summary>
/// <remarks>
///     <para>
///     <c>Ui.ModalTrigger</c> names its dialog twice — <c>command="show-modal" commandfor</c> AND
///     <c>popovertarget</c> — on a <c>&lt;dialog popover&gt;</c>, so a browser with invoker commands opens a real
///     modal and one without opens a popover. That only works if three platform facts hold: the invoker command
///     wins over the popover action on one button, <c>showModal()</c> accepts a dialog that carries a popover
///     attribute, and a modal opened that way makes the page inert and hands focus back. None of that is
///     observable from markup, and a browser update that changed any of it would turn every kit modal back into
///     a popover without a single failing unit test.
///     </para>
///     <para>
///     Two more hold up what <c>UiModal</c> does without a script of its own: <c>closedby</c> decides whether a
///     click outside and Escape close it, and raises <c>cancel</c> before <c>close</c> when they do; and an
///     <c>autofocus</c> element inside takes the focus the dialog would otherwise hand its first control.
///     </para>
///     <para>
///     The markup is the shape <c>UiModal</c> renders, written out rather than rendered, because this project does
///     not reference the kit: what is under test is the browser. The component itself is driven end to end by
///     <c>UiKitActionsTests</c>.
///     </para>
/// </remarks>
public sealed class DialogInvokerContractTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Markup = """
        <!doctype html>
        <html lang="en"><body style="height:3000px">
          <button id="before">before</button>
          <button id="open" type="button" command="show-modal" commandfor="dlg" popovertarget="dlg">Open</button>
          <dialog id="dlg" popover="auto" closedby="any" data-modal="dlg" aria-label="Shortcuts" style="padding:24px">
            <div id="placeholder" tabindex="-1" data-ui-focus-placeholder autofocus></div>
            <button id="first" type="button">First</button>

            <button id="close" type="button" command="close" commandfor="dlg" popovertarget="dlg" popovertargetaction="hide">Close</button>
          </dialog>
          <button id="after">after</button>
        </body></html>
        """;

    [Fact]
    public async Task The_invoker_command_opens_a_popover_dialog_as_a_real_modal_and_hands_focus_back()
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.SetContentAsync(Markup);

        var dialog = page.Locator("#dlg");
        await page.Locator("#open").FocusAsync();
        await page.Keyboard.PressAsync("Enter");

        await Expect(dialog).ToBeVisibleAsync();
        // :modal, not :popover-open — the command won over the popover action on the same button.
        Assert.True(await dialog.EvaluateAsync<bool>("d => d.matches(':modal')"), "the dialog opened as a popover, not a modal");
        Assert.False(await dialog.EvaluateAsync<bool>("d => d.matches(':popover-open')"));

        // The page behind is inert: Tab from the last control inside wraps to the document, never to #after.
        for (var i = 0; i < 4; i++)
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
    public async Task The_close_command_closes_the_modal()
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.SetContentAsync(Markup);

        await page.ClickAsync("#open");
        await Expect(page.Locator("#dlg")).ToBeVisibleAsync();

        await page.ClickAsync("#close");
        await Expect(page.Locator("#dlg")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task Closedby_none_keeps_a_modal_open_on_escape()
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.SetContentAsync(Markup.Replace("popover=\"auto\" closedby=\"any\"", "popover=\"manual\" closedby=\"none\"", StringComparison.Ordinal));

        await page.ClickAsync("#open");
        await Expect(page.Locator("#dlg")).ToBeVisibleAsync();

        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(300);
        await Expect(page.Locator("#dlg")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Closedby_any_closes_on_a_click_outside_and_raises_cancel_before_close()
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.SetContentAsync(Markup);
        await page.EvaluateAsync(
            "() => { window.heard = []; for (const type of ['cancel', 'close']) dlg.addEventListener(type, () => window.heard.push(type)); }");

        await page.ClickAsync("#open");
        await Expect(page.Locator("#dlg")).ToBeVisibleAsync();
        await page.Mouse.ClickAsync(5, 5);

        // Ui.Modal.OnCancel is this cancel and OnClose what follows it, with no handler of the kit's on the
        // backdrop. The close event is queued behind the closing itself, so it is waited for rather than read.
        await Expect(page.Locator("#dlg")).ToBeHiddenAsync();
        await page.WaitForFunctionAsync("() => window.heard.includes('close')");
        Assert.Equal("cancel,close", await page.EvaluateAsync<string>("() => window.heard.join(',')"));

    }

    [Fact]
    public async Task Closedby_closerequest_ignores_a_click_outside_and_still_closes_on_escape()
    {
        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.SetContentAsync(Markup.Replace("closedby=\"any\"", "closedby=\"closerequest\"", StringComparison.Ordinal));

        await page.ClickAsync("#open");
        await page.Mouse.ClickAsync(5, 5);
        await page.WaitForTimeoutAsync(300);
        await Expect(page.Locator("#dlg")).ToBeVisibleAsync();

        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator("#dlg")).ToBeHiddenAsync();
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
