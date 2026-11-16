using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     <c>.Confirm("…")</c> and <c>ConfirmLeave("…")</c> on the WebAssembly host, through the forms guide's demo, the UI
///     tab's modal example, and the UI tab's Confirm and Confirm leave pages with the demo they frame: the site's
///     layouts place <c>Ui.Confirm</c>, so the question is asked in that dialog before the click reaches .NET, and a
///     refused one changes nothing. The Server host's half is <c>ConfirmTests</c> and <c>ConfirmDialogTests</c> in
///     <c>Rask.Server.E2E.Tests</c>, which also hold the browser's own dialog where no layout placed one.
/// </summary>
public sealed class ConfirmWasmTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    private const string Dialog = "#ui-confirm";
    private const string Yes = Dialog + " [data-rask-confirm-dialog=go]";
    private const string No = Dialog + " .flex button:not([data-rask-confirm-dialog])";
    private const string Field = Dialog + " [data-rask-confirm-dialog=field] input";

    protected override string BaseUrl => app.BaseUrl;

    protected override string FixtureName => "Wasm";

    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task No_keeps_the_row_and_Yes_deletes_it() => RunAsync(async () =>
    {
        var browserDialogs = Count();
        await OpenAsync();
        var delete = Page.Locator("#fc-delete-Acme");

        await delete.ClickAsync();
        await Expect(Page.Locator(Dialog + " [data-rask-confirm-dialog=message]")).ToHaveTextAsync("Delete Acme?");
        await Page.ClickAsync(No);
        await Expect(Page.Locator(Dialog)).ToBeHiddenAsync();
        await Expect(Page.Locator("#fc-left")).ToHaveTextAsync("3 partners");
        await delete.ClickAsync();
        await Page.ClickAsync(Yes);

        await Expect(Page.Locator("#fc-left")).ToHaveTextAsync("2 partners", new() { Timeout = 10_000 });
        await Expect(delete).ToHaveCountAsync(0);
        Assert.Empty(browserDialogs);
    });

    [Fact]
    public Task Yes_waits_for_the_name_typed_exactly() => RunAsync(async () =>
    {
        var browserDialogs = Count();
        await OpenAsync();

        await Page.ClickAsync("#fc-wipe-Globex");
        await Expect(Page.Locator(Field)).ToBeFocusedAsync();
        await Page.Keyboard.TypeAsync("globex");
        var disabledInAnotherCase = await Page.Locator(Yes).IsDisabledAsync();
        await Page.FillAsync(Field, "Globex");
        await Page.ClickAsync(Yes);

        await Expect(Page.Locator("#fc-left")).ToHaveTextAsync("2 partners", new() { Timeout = 10_000 });
        Assert.True(disabledInAnotherCase);
        Assert.Empty(browserDialogs);
    });

    [Fact]
    public Task The_ui_tabs_example_asks_in_the_kits_dialog_and_Escape_calls_it_off() => RunAsync(async () =>
    {
        var browserDialogs = Count();
        await OpenUiPageAsync("modal", "Modal");
        var state = Page.Locator("#ui-confirm-state");

        await Page.ClickAsync("#ui-confirm-delete");
        await Expect(Page.Locator(Dialog)).ToBeVisibleAsync();
        await Expect(Page.Locator(Dialog + " [data-rask-confirm-dialog=heading]")).ToHaveTextAsync("Delete project?");
        await Page.Keyboard.PressAsync("Escape");
        await Expect(Page.Locator(Dialog)).ToBeHiddenAsync();
        await Expect(state).ToHaveTextAsync("The Rask project is still there.");
        await Page.ClickAsync("#ui-confirm-delete");
        await Page.ClickAsync(Yes);

        await Expect(state).ToHaveTextAsync("You deleted the project.", new() { Timeout = 10_000 });
        Assert.Empty(browserDialogs);
    });

    [Fact]
    public Task The_confirm_page_asks_before_a_delete_and_before_a_form_is_sent() => RunAsync(async () =>
    {
        var browserDialogs = Count();
        await OpenUiPageAsync("confirm", "Confirm");
        var state = Page.Locator("#uc-state");

        await Page.ClickAsync("#uc-delete");
        await Expect(Page.Locator(Dialog + " [data-rask-confirm-dialog=heading]")).ToHaveTextAsync("Delete project?");
        await Page.ClickAsync(No);
        await Expect(Page.Locator(Dialog)).ToBeHiddenAsync();
        await Expect(state).ToHaveTextAsync("The Rask project is still there.");
        await Page.ClickAsync("#uc-send");
        await Expect(Page.Locator(Dialog + " [data-rask-confirm-dialog=heading]")).ToHaveTextAsync("Send the order?");
        await Page.ClickAsync(Yes);

        await Expect(Page.Locator("#uc-sent")).ToHaveTextAsync("Sent: Pancakes", new() { Timeout = 10_000 });
        Assert.Empty(browserDialogs);
    });

    [Fact]
    public Task A_sign_out_link_asks_in_the_catalogs_words_and_is_followed_only_after_a_yes() => RunAsync(async () =>
    {
        var browserDialogs = Count();
        await OpenUiPageAsync("confirm", "Confirm");
        var signOut = Page.GetByTestId("ui-confirm-sign-out").GetByRole(AriaRole.Menuitem, new() { Name = "Sign out" });

        await Page.ClickAsync("#uc-account");
        await signOut.ClickAsync();
        await Expect(Page.Locator(Dialog + " [data-rask-confirm-dialog=heading]")).ToHaveTextAsync("Sign out");
        await Expect(Page.Locator(Dialog + " [data-rask-confirm-dialog=message]")).ToHaveTextAsync("Are you sure you want to sign out?");
        await Page.ClickAsync(No);
        await Expect(Page.Locator(Dialog)).ToBeHiddenAsync();
        var stayed = Page.Url;
        await Page.ClickAsync("#uc-account");
        await signOut.ClickAsync();
        await Page.ClickAsync(Yes);

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("#signed-out$"), new() { Timeout = 10_000 });
        Assert.DoesNotContain("#signed-out", stayed, StringComparison.Ordinal);
        Assert.Empty(browserDialogs);
    });

    [Fact]
    public Task A_layouts_own_words_name_the_dialog_and_its_buttons() => RunAsync(async () =>
    {
        var browserDialogs = Count();
        await Page.GotoAsync("/demo/confirm-words/");
        await WaitForInteractiveAsync();

        await Page.ClickAsync("#uc-words-archive");
        await Expect(Page.Locator(Dialog + " [data-rask-confirm-dialog=heading]")).ToHaveTextAsync("Megerősítés");
        await Page.Locator(Dialog).GetByRole(AriaRole.Button, new() { Name = "Nem" }).ClickAsync();
        await Expect(Page.Locator(Dialog)).ToBeHiddenAsync();
        await Page.ClickAsync("#uc-words-delete");
        await Expect(Page.Locator(Dialog + " [data-rask-confirm-dialog=heading]")).ToHaveTextAsync("Cég törlése");
        await Page.Locator(Dialog).GetByRole(AriaRole.Button, new() { Name = "Igen" }).ClickAsync();

        await Expect(Page.Locator("#uc-words-left")).ToHaveTextAsync("2 cég", new() { Timeout = 10_000 });
        Assert.Empty(browserDialogs);
    });

    [Fact]
    public Task The_confirm_leave_page_keeps_an_unsaved_note_when_the_reader_stays() => RunAsync(async () =>
    {
        var browserDialogs = Count();
        await OpenUiPageAsync("confirm-leave", "Confirm leave");
        var leave = Page.Locator("#ui-confirm-leave");

        await Page.Locator("#ucl-input").FillAsync("remember the milk");
        await Page.Locator("[data-ui-sidebar] nav[data-ui-navlist]").GetByRole(AriaRole.Link, new() { Name = "Modal", Exact = true }).ClickAsync();
        await Expect(leave).ToBeVisibleAsync();
        await Expect(leave.Locator("h2")).ToHaveTextAsync("Leave without saving your note?");
        await leave.GetByRole(AriaRole.Button, new() { Name = "Stay" }).ClickAsync();
        await Expect(leave).ToBeHiddenAsync();
        await Expect(Page.Locator("#ucl-input")).ToHaveValueAsync("remember the milk");
        Assert.Contains("/ui/confirm-leave", Page.Url, StringComparison.Ordinal);
        await Page.ClickAsync("#ucl-save");

        await Expect(Page.Locator("#ucl-saved")).ToHaveTextAsync("Saved: remember the milk", new() { Timeout = 10_000 });
        await Expect(Page.Locator("#ucl-form")).ToHaveAttributeAsync("data-rask-saved", "1");
        Assert.Empty(browserDialogs);
    });

    // Every browser dialog that appears: with the kit's dialog on the page, one appearing is the bug.
    private List<string> Count()
    {
        List<string> asked = [];
        Page.Dialog += (_, dialog) =>
        {
            asked.Add(dialog.Type + ":" + dialog.Message);
            _ = dialog.DismissAsync();
        };
        return asked;
    }

    private async Task OpenAsync()
    {
        await Page.GotoAsync(Docs + "/guides/forms");
        await WaitForInteractiveAsync();
        var demo = Page.Locator("#fc-demo");
        await Expect(demo).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await demo.ScrollIntoViewIfNeededAsync();
    }
}
