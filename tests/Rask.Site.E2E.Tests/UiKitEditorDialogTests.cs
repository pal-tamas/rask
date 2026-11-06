using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     <c>Ui.EditorDialog.In("edit")</c> on the UI tab (#1295): the editor over its list, opened by the query string — a
///     link, a typed address, Back — and closed with the list's own filters kept.
/// </summary>
public sealed class UiKitEditorDialogTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    private const string Scope = "[data-testid='ui-editor-dialog']";
    private const string Dialog = Scope + " dialog";

    protected override string BaseUrl => app.BaseUrl;

    protected override string FixtureName => "Wasm";

    protected override string ServerLog => app.ServerLog;

    [Fact]
    public Task An_address_with_the_key_opens_the_record_it_names() => RunAsync(async () =>
    {
        await OpenAsync("?q=o&edit=2");

        var dialog = Page.Locator(Dialog);

        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Expect(dialog.Locator("h2")).ToHaveTextAsync("Edit supplier");
        await Expect(dialog.GetByLabel("Name")).ToHaveValueAsync("Bothman & Sons");
    });

    [Fact]
    public Task New_opens_it_over_the_list_and_the_cross_closes_it_with_the_search_kept() => RunAsync(async () =>
    {
        await OpenAsync("?q=o");
        var dialog = Page.Locator(Dialog);
        var add = Page.Locator(Scope + " a[data-ui-button]", new() { HasText = "New supplier" });
        await add.ScrollIntoViewIfNeededAsync();

        await Expect(add).ToHaveAttributeAsync("href", new Regex(@"/ui/editor-dialog/?\?q=o&edit=new$"));
        await add.ClickAsync();
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Expect(dialog.Locator("h2")).ToHaveTextAsync("New supplier");
        await Expect(Page).ToHaveURLAsync(new Regex(@"\?q=o&edit=new$"));
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Close modal" }).ClickAsync();

        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 10_000 });
        await Expect(Page).ToHaveURLAsync(new Regex(@"/ui/editor-dialog/?\?q=o$"));
        await Expect(Page.Locator(Scope + " input[placeholder='Search suppliers']")).ToHaveValueAsync("o");
    });

    [Fact]
    public Task Back_closes_the_dialog_the_rows_edit_opened() => RunAsync(async () =>
    {
        await OpenAsync("?q=o");
        var dialog = Page.Locator(Dialog);
        var levin = Page.Locator(Scope + " #ued-suppliers tbody tr", new() { HasText = "Levin Oil" });
        await levin.ScrollIntoViewIfNeededAsync();

        await EditAsync(levin);
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Expect(dialog.GetByLabel("Name")).ToHaveValueAsync("Levin Oil");
        await Page.GoBackAsync();

        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 10_000 });
        await Expect(Page).ToHaveURLAsync(new Regex(@"/ui/editor-dialog/?\?q=o$"));
    });

    [Fact]
    public Task A_save_closes_it_and_the_list_shows_the_record_as_saved() => RunAsync(async () =>
    {
        await OpenAsync("?q=o");
        var dialog = Page.Locator(Dialog);
        var rows = Page.Locator(Scope + " #ued-suppliers tbody tr");
        var geidt = rows.Filter(new() { HasText = "Geidt Logistics" });
        await geidt.ScrollIntoViewIfNeededAsync();

        await EditAsync(geidt);
        await Expect(dialog).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await dialog.GetByLabel("City").FillAsync("Lisbon");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await Expect(dialog).ToBeHiddenAsync(new() { Timeout = 10_000 });
        await Expect(geidt).ToContainTextAsync("Lisbon");
        await Expect(Page).ToHaveURLAsync(new Regex(@"/ui/editor-dialog/?\?q=o$"));
    });

    [Fact]
    public Task A_wide_one_is_a_sheet_as_wide_as_the_screen_allows() => RunAsync(async () =>
    {
        await Page.SetViewportSizeAsync(1280, 800);
        await OpenAsync("?delivery=new");

        var sheet = Page.Locator("[data-testid='ui-editor-dialog-wide'] dialog");
        await Expect(sheet).ToBeVisibleAsync(new() { Timeout = 10_000 });
        // The flyout slides in from the right: measured mid-slide it stands short of the edge.
        // Wait for its own finite transitions (not a spinner's endless one inside it), then for it to hold still.
        await sheet.EvaluateAsync(
            """
            async el => {
                await Promise.all(el.getAnimations().filter(a => a.effect?.getTiming().iterations !== Infinity).map(a => a.finished));
                await new Promise(done => {
                    let last;
                    const step = () => { const x = el.getBoundingClientRect().x; if (x === last) done(); else { last = x; requestAnimationFrame(step); } };
                    requestAnimationFrame(step);
                });
            }
            """);
        var box = await sheet.BoundingBoxAsync();

        Assert.NotNull(box);
        Assert.True(box.Width >= 1200, $"the sheet is {box.Width}px wide on a 1280px screen");
        Assert.True(Math.Abs(box.X + box.Width - 1280) < 20, $"the sheet does not stand against the right edge: x={box.X}");
        await Expect(Page.Locator(Dialog)).ToBeHiddenAsync();
    });

    // The docs page at an address of the reader's own: the query string is what opens the editor.
    private async Task OpenAsync(string query)
    {
        await Page.GotoAsync($"{UiTab}/editor-dialog/{query}");
        await Expect(Page.Locator("main h1")).ToContainTextAsync("Editor dialog", new() { Timeout = 30_000 });
        await WaitForInteractiveAsync();
    }

    // A row opens its editor from its ⋯ menu's Edit, a link to the list's own address with the key set.
    private static async Task EditAsync(ILocator row)
    {
        await row.GetByRole(AriaRole.Button, new() { Name = "Actions" }).ClickAsync();
        await row.GetByRole(AriaRole.Menuitem, new() { Name = "Edit" }).ClickAsync();
    }
}
