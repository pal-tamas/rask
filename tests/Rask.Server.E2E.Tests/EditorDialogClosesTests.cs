using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Core.Routing;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A list's editor dialog at the list's own address (<c>?edit=</c>), drawn as the apps that reported it draw it:
///     open while the address names a row, its close taking the key out (<c>Go.To(list).Replacing()</c>), its cancel a
///     link back to the list, its form guarded by <c>Ui.ConfirmLeave</c>. Every way the reader closes it closes it,
///     every time.
/// </summary>
/// <remarks>
///     Found as "the cross and Cancel do not always close it" (2026-11-09): a double press on the cross with something
///     typed or picked. The first press asks the leave guard's question; the second landed on that question's backdrop
///     and was taken as a press outside, which answered "stay" before the question had faded in.
/// </remarks>
public sealed class EditorDialogClosesTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Editor = "dialog[data-rask-modal-open]";

    private const string Cross = Editor + " button[aria-label='Close modal']";

    private const string Cancel = "[data-ui-form-buttons] a";

    private const string Leave = "#ui-confirm-leave";

    [Theory]
    [InlineData("x")]
    [InlineData("cancel")]
    public async Task A_double_press_on_an_edited_editor_asks_once_and_leaving_closes_it_every_time(string way)
    {
        await using var session = await OpenAsync("");
        var page = session.Page;

        for (var cycle = 0; cycle < 10; cycle++)
        {
            await OpenRowAsync(page, Row(cycle % 3));
            await page.FillAsync("#name", "changed");

            await DoublePressAsync(page, way == "x" ? Cross : Cancel);
            await Expect(page.Locator(Leave)).ToBeVisibleAsync();
            await page.ClickAsync(Leave + " [data-rask-leave=go]");

            await Expect(page.Locator(Editor)).ToBeHiddenAsync();
            await Expect(page).ToHaveURLAsync(session.BaseUrl + "/drivers");
        }
    }

    [Theory]
    [InlineData("x", "")]
    [InlineData("cancel", "")]
    [InlineData("escape", "")]
    [InlineData("typed-x", "")]
    [InlineData("typed-cancel", "")]
    [InlineData("typed-escape", "")]
    [InlineData("double-x", "")]
    [InlineData("x", "around")]
    [InlineData("cancel", "around")]
    [InlineData("typed-x", "around")]
    public async Task The_editor_dialog_closes_every_time_the_reader_closes_it(string way, string ticking)
    {
        await using var session = await OpenAsync(ticking);
        var page = session.Page;

        for (var cycle = 0; cycle < 10; cycle++)
        {
            await OpenRowAsync(page, Row(cycle % 3));

            await CloseAsync(page, way);

            await Expect(page.Locator(Editor)).ToBeHiddenAsync();
            await Expect(page).ToHaveURLAsync(session.BaseUrl + "/drivers");
            await Expect(page.Locator(Leave)).ToBeHiddenAsync();
        }
    }

    [Theory]
    [InlineData(1, "")]
    [InlineData(4, "around")]
    public async Task A_reader_working_at_random_closes_the_editor_dialog_every_time(int seed, string ticking)
    {
        await using var session = await OpenAsync(ticking);
        var page = session.Page;
        var random = new Random(seed);
        var done = new List<string>();

        for (var cycle = 0; cycle < 25; cycle++)
        {
            done.Clear();
            var row = Row(random.Next(3));
            done.Add("open " + row);
            await OpenRowAsync(page, row);
            await EditAtRandomAsync(page, random, done);
            var press = new[] { "x", "cancel", "escape", "double-x", "double-cancel" }[random.Next(5)];
            done.Add(press);

            await PressAsync(page, press);
            await AnswerAtRandomAsync(page, random, done);

            try
            {
                await Expect(page.Locator(Editor)).ToBeHiddenAsync(new() { Timeout = 4000 });
            }
            catch (PlaywrightException e)
            {
                Assert.Fail("The editor stayed open after: " + string.Join(", ", done) + Environment.NewLine + e.Message);
            }

            await Expect(page).ToHaveURLAsync(session.BaseUrl + "/drivers");
        }
    }

    private async Task<HookSession> OpenAsync(string ticking)
    {
        EditorDialogScene.Ticking = ticking;
        var session = await HookSession.OpenAsync<EditorDialogApp>(playwright, path: "/drivers");
        await session.HooksLoadedAsync();
        await Expect(session.Page.Locator("#rows")).ToHaveTextAsync("r0 r1 r2");
        return session;
    }

    private static string Row(int n) => "r" + n.ToString(CultureInfo.InvariantCulture);

    private static async Task OpenRowAsync(IPage page, string row)
    {
        await page.ClickAsync("#edit-" + row);
        await Expect(page.Locator(Editor)).ToBeVisibleAsync();
        await Expect(page.Locator("#name")).ToHaveValueAsync(row);
    }

    private static async Task CloseAsync(IPage page, string way)
    {
        var typed = way.StartsWith("typed-", StringComparison.Ordinal);
        if (typed)
        {
            await page.FillAsync("#name", "changed");
        }

        await PressAsync(page, way.Replace("typed-", "", StringComparison.Ordinal));

        if (typed)
        {
            await page.ClickAsync(Leave + " [data-rask-leave=go]");
        }
    }

    // A double press as a hand makes one: the second press a beat after the first, where the first one was — by
    // then on whatever the first press put there.
    private static async Task DoublePressAsync(IPage page, string selector)
    {
        var box = await page.Locator(selector).BoundingBoxAsync();
        Assert.NotNull(box);
        var (x, y) = (box.X + (box.Width / 2), box.Y + (box.Height / 2));
        await page.Mouse.MoveAsync(x, y);
        await page.Mouse.DownAsync(new() { ClickCount = 1 });
        await page.Mouse.UpAsync(new() { ClickCount = 1 });
        await page.WaitForTimeoutAsync(120);
        await page.Mouse.DownAsync(new() { ClickCount = 2 });
        await page.Mouse.UpAsync(new() { ClickCount = 2 });
    }

    private static async Task PressAsync(IPage page, string press)
    {
        switch (press)
        {
            case "x":
                await page.ClickAsync(Cross);
                break;
            case "cancel":
                await page.ClickAsync(Cancel);
                break;
            case "double-x":
                await DoublePressAsync(page, Cross);
                break;
            case "double-cancel":
                await DoublePressAsync(page, Cancel);
                break;
            default:
                await page.FocusAsync("#name");
                await page.Keyboard.PressAsync("Escape");
                break;
        }
    }

    // Typed into, picked in a list the page draws, a save the model refused: each now and then.
    private static async Task EditAtRandomAsync(IPage page, Random random, List<string> done)
    {
        if (random.Next(3) == 0)
        {
            done.Add("type");
            await page.FillAsync("#name", "changed");
        }

        if (random.Next(3) == 0)
        {
            done.Add("pick");
            await page.ClickAsync("#kind [data-ui-select-button]");
            await page.Locator("#kind [data-ui-option]").Nth(random.Next(2)).ClickAsync();
        }

        if (random.Next(5) == 0)
        {
            done.Add("refused save");
            await page.FillAsync("#name", "");
            await page.ClickAsync("[data-ui-form-buttons] button[type=submit]");
            await Expect(page.Locator("#name")).ToHaveAttributeAsync("aria-invalid", "true");
        }
    }

    // Asked whether to leave: staying once now and then, and closing again with the cross; then leaving.
    private static async Task AnswerAtRandomAsync(IPage page, Random random, List<string> done)
    {
        for (var asked = 0; asked < 3; asked++)
        {
            try
            {
                await Expect(page.Locator(Leave)).ToBeVisibleAsync(new() { Timeout = 500 });
            }
            catch (PlaywrightException)
            {
                return;
            }

            if (asked == 0 && random.Next(3) == 0)
            {
                done.Add("stay, then x");
                await page.ClickAsync(Leave + " .flex button:not([data-rask-leave])");
                await Expect(page.Locator(Leave)).ToBeHiddenAsync();
                await page.ClickAsync(Cross);
                continue;
            }

            done.Add("leave");
            await page.ClickAsync(Leave + " [data-rask-leave=go]");
        }
    }
}

public sealed class EditorDialogRow
{
    [Required]
    public string Name { get; set; } = "";

    public string? Kind { get; set; }
}

/// <summary>What pushes renders while the reader works: nothing, or the app around the page every 100 ms.</summary>
public static class EditorDialogScene
{
    private static volatile string _ticking = "";

    public static string Ticking
    {
        get => _ticking;
        set => _ticking = value;
    }
}

/// <summary>The app as the reporting apps lay it out: a router, and the leave guard's dialog beside it.</summary>
public sealed partial class EditorDialogApp : Component
{
    private PumpTicker? _ticker;
    private int _ticks;

    protected override Component? HeadAssets => Markup.Title["editor dialog"];

    protected override string? HtmlLang => "en";

    protected override Task OnMount()
    {
        if (EditorDialogScene.Ticking == "around")
        {
            _ticker = new PumpTicker(() =>
            {
                _ticks++;
                StateHasChanged();
            });
        }

        return Task.CompletedTask;
    }

    protected override Task OnUnmount()
    {
        _ticker?.Dispose();
        return Task.CompletedTask;
    }

    protected override Component? Render() =>
    [
        P.Id("ticks")[_ticks.ToString(CultureInfo.InvariantCulture)],
        Router,
        Ui.ConfirmLeave.Stay("Stay").Leave("Leave")
    ];
}

/// <summary>A list at <c>/drivers</c> whose rows open their editor over it at <c>?edit=</c>, as a flyout.</summary>
[Route("/drivers")]
public sealed partial class EditorDialogListPage : Component
{
    [QueryParam("edit")] public string? Edit { get; set; }

    private static RouteUrl Here => Routes.EditorDialogListPage();

    protected override Component? Render() =>
    [
        P.Id("rows")["r0 r1 r2"],
        Ul[new[] { "r0", "r1", "r2" }.Select(row => Li.Key(row)[Ui.Button.Id("edit-" + row).Href(Here.With("edit", row))[row]])],
        For(Edit, EditorDialogLoad.Key(Edit ?? "closed").Editing(Edit ?? "new").Back(Here), Here)
    ];

    // The apps' own EditorDialog.For, as they wrote it.
    private static Component For(string? editing, Component? content, RouteUrl list) =>
        Ui.Modal.Key("editor").Flyout(true).Open(editing is not null)
            .OnClose(() => { if (editing is not null) Go.To(list).Replacing(); })[
            editing is null ? null : content
        ];
}

/// <summary>Reads the record, a moment after it opens, then shows its form.</summary>
public sealed partial class EditorDialogLoad : Component
{
    private EditorDialogRow? _row;

    public string Editing { get; set; } = "new";

    public RouteUrl? Back { get; set; }

    protected override async Task OnMount()
    {
        await Task.Delay(40);
        _row = new EditorDialogRow { Name = Editing };
    }

    protected override Component? Render() =>
        _row is null ? null : EditorDialogForm.Row(_row).Back(Back ?? Routes.EditorDialogListPage());
}

/// <summary>The form: guarded, a list the page draws, and the kit's buttons, whose cancel is a link back to the list.</summary>
public sealed partial class EditorDialogForm : Component
{
    public required EditorDialogRow Row { get; set; }

    public RouteUrl Back { get; set; }

    protected override Component? Render() =>
        Form.Model(Row).OnSubmit(Save).ConfirmLeave("Discard?").Class("space-y-6")[
            Ui.Input.Bind(() => Row.Name).Id("name").Label("Name"),
            Div.Id("kind")[
                Ui.Select.Bind(() => Row.Kind).Label("Kind").Listbox.Placeholder("Any")[
                    Ui.SelectOption.Value("a")["Alpha"],
                    Ui.SelectOption.Value("b")["Beta"]
                ]
            ],
            Ui.FormButtons.Back(Back)
        ];

    private void Save(EditorDialogRow row) => Go.To(Back);
}
