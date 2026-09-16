using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Server.E2E.Tests;

/// <summary>
///     The platform behaviour Rask UI's modal is built on, pinned in a real browser.
/// </summary>
/// <remarks>
///     <para>
///     <c>UiModal</c>'s trigger names its dialog twice — <c>command="show-modal" commandfor</c> AND
///     <c>popovertarget</c> — on a <c>&lt;dialog popover&gt;</c>, so a browser with invoker commands opens a real
///     modal and one without opens a popover. That only works if three platform facts hold: the invoker command
///     wins over the popover action on one button, <c>showModal()</c> accepts a dialog that carries a popover
///     attribute, and a modal opened that way makes the page inert and hands focus back. None of that is
///     observable from markup, and a browser update that changed any of it would turn every kit modal back into
///     a popover without a single failing unit test.
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
          <dialog id="dlg" popover="auto" aria-label="Shortcuts">
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
        await page.SetContentAsync(Markup.Replace("popover=\"auto\"", "popover=\"manual\" closedby=\"none\"", StringComparison.Ordinal));

        await page.ClickAsync("#open");
        await Expect(page.Locator("#dlg")).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(300);
        await Expect(page.Locator("#dlg")).ToBeVisibleAsync();
    }
}
