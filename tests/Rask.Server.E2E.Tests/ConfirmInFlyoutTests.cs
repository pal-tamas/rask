using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A <c>.Confirm("…")</c> delete inside a <c>Ui.Modal</c> flyout holding a guarded form, the shape of an edit
///     sheet: yes runs the handler exactly once and no runs nothing, asked in <c>Ui.Confirm</c> or in the browser's
///     own dialog — and whatever the page renders while the reader is being asked. A delete drawn anew in the
///     meantime is still the one said yes to; another action drawn where it was is never run in its place.
/// </summary>
public sealed class ConfirmInFlyoutTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Dialog = "#ui-confirm";
    private const string Yes = Dialog + " [data-rask-confirm-dialog=go]";
    private const string No = Dialog + " .flex button:not([data-rask-confirm-dialog])";
    private const string Delete = "button[data-rask-confirm]";

    [Theory]
    [InlineData(null, false)]
    [InlineData(SheetChange.Rendered, false)]
    [InlineData(SheetChange.Redrawn, false)]
    [InlineData(null, true)]
    [InlineData(SheetChange.Rendered, true)]
    [InlineData(SheetChange.Redrawn, true)]
    public async Task Yes_in_the_kit_dialog_runs_the_delete_once(SheetChange? whileAsked, bool typedFirst)
    {
        await using var session = await OpenAsync<ConfirmFlyoutDialogPage>();
        var page = session.Page;
        await OpenFlyoutAsync(page);
        if (typedFirst) await page.FillAsync("#item", "Kút 3");

        await page.ClickAsync(Delete);
        await Expect(page.Locator(Dialog)).ToBeVisibleAsync();
        if (whileAsked is { } change) await RenderAgainAsync(session, change);
        await page.ClickAsync(Yes);

        await Expect(page.Locator("#deleted")).ToHaveTextAsync("deleted=1");
        await page.WaitForTimeoutAsync(300);
        await Expect(page.Locator("#deleted")).ToHaveTextAsync("deleted=1");
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(SheetChange.Rendered)]
    [InlineData(SheetChange.Redrawn)]
    public async Task No_in_the_kit_dialog_runs_nothing(SheetChange? whileAsked)
    {
        await using var session = await OpenAsync<ConfirmFlyoutDialogPage>();
        var page = session.Page;
        await OpenFlyoutAsync(page);

        await page.ClickAsync(Delete);
        await Expect(page.Locator(Dialog)).ToBeVisibleAsync();
        if (whileAsked is { } change) await RenderAgainAsync(session, change);
        await page.ClickAsync(No);
        await Expect(page.Locator(Dialog)).ToBeHiddenAsync();
        await page.WaitForTimeoutAsync(300);

        await Expect(page.Locator("#deleted")).ToHaveTextAsync("deleted=0");
        Assert.True(await page.Locator("#well").EvaluateAsync<bool>("d => d.matches(':modal')"));
    }

    [Fact]
    public async Task Yes_runs_nothing_once_another_action_took_the_deletes_place()
    {
        await using var session = await OpenAsync<ConfirmFlyoutDialogPage>();
        var page = session.Page;
        await OpenFlyoutAsync(page);
        var handler = await page.Locator(Delete).GetAttributeAsync("data-rask-on-click");

        await page.ClickAsync(Delete);
        await Expect(page.Locator(Dialog)).ToBeVisibleAsync();
        await RenderAgainAsync(session, SheetChange.Swapped);
        var taken = await page.Locator("button:has-text('Archiválás')").GetAttributeAsync("data-rask-on-click");
        await page.ClickAsync(Yes);
        await Expect(page.Locator(Dialog)).ToBeHiddenAsync();
        await page.WaitForTimeoutAsync(500);

        // The archive took the delete's very id: a yes that went by the id alone would run it.
        Assert.Equal(handler, taken);
        await Expect(page.Locator("#deleted")).ToHaveTextAsync("deleted=0");
        await Expect(page.Locator("#archived")).ToHaveTextAsync("archived=0");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task The_browsers_own_dialog_runs_the_delete_only_on_yes(bool yes, bool renderedWhileAsked)
    {
        await using var session = await OpenAsync<ConfirmFlyoutFallbackPage>();
        var page = session.Page;
        await OpenFlyoutAsync(page);
        var asked = 0;
        page.Dialog += (_, dialog) =>
        {
            asked++;
            _ = Answer(dialog, yes, renderedWhileAsked);
        };

        await page.ClickAsync(Delete);
        await page.WaitForTimeoutAsync(300);

        await Expect(page.Locator("#deleted")).ToHaveTextAsync(yes ? "deleted=1" : "deleted=0");
        Assert.Equal(1, asked);
    }

    // The server renders again, the delete drawn anew, while the browser's dialog holds the page still.
    private static async Task Answer(IDialog dialog, bool yes, bool renderedWhileAsked)
    {
        if (renderedWhileAsked) ConfirmFlyoutPulse.Beat(SheetChange.Redrawn);
        await Task.Delay(renderedWhileAsked ? 200 : 0);
        await (yes ? dialog.AcceptAsync() : dialog.DismissAsync());
    }

    // A render the reader did not ask for — a poll, a toast arriving — that reaches the open flyout.
    private static async Task RenderAgainAsync(HookSession session, SheetChange change)
    {
        ConfirmFlyoutPulse.Beat(change);
        await Expect(session.Page.Locator("#beats")).ToHaveTextAsync("beats=1");
    }

    private static async Task OpenFlyoutAsync(IPage page)
    {
        await page.ClickAsync("#open");
        await Expect(page.Locator("#well")).ToBeVisibleAsync();
        await Expect(page.Locator(Delete)).ToBeVisibleAsync();
    }

    private async Task<HookSession> OpenAsync<TPage>()
        where TPage : Component
    {
        var session = await HookSession.OpenAsync<TPage>(playwright);
        await session.HooksLoadedAsync();
        return session;
    }
}

/// <summary>What a render the reader did not ask for changes in the open sheet.</summary>
public enum SheetChange
{
    /// <summary>Text, and a line above the delete: the delete stays the element it was.</summary>
    Rendered = 1,

    /// <summary>The delete drawn as a new element, as a render that rebuilds the sheet does.</summary>
    Redrawn,

    /// <summary>The delete gone, and another action drawn where it was.</summary>
    Swapped,
}

/// <summary>A beat a test sends the open sheet from outside it. Only <see cref="ConfirmInFlyoutTests" /> uses it, one test at a time.</summary>
public static class ConfirmFlyoutPulse
{
    public static event EventHandler<SheetChange>? Beats;

    public static void Beat(SheetChange change) => Beats?.Invoke(null, change);
}

/// <summary>A flyout edit sheet: a guarded form, and a delete that asks first; it renders again on every beat.</summary>
public sealed partial class ConfirmFlyoutSheet : Component
{
    private readonly ConfirmOrder _well = new();
    private bool _open;
    private int _deleted;
    private int _archived;
    private int _beats;
    private SheetChange? _change;

    protected override Task OnMount()
    {
        ConfirmFlyoutPulse.Beats += Beat;
        return Task.CompletedTask;
    }

    protected override Task OnUnmount()
    {
        ConfirmFlyoutPulse.Beats -= Beat;
        return Task.CompletedTask;
    }

    protected override Component? Render() =>
    [
        _beats > 0 ? Div.Class("notice")["A new well was reported."] : null,
        P.Id("deleted")[$"deleted={_deleted}"],
        P.Id("archived")[$"archived={_archived}"],
        P.Id("beats")[$"beats={_beats}"],
        Button.Id("open").OnClick(() => _open = true)["open"],
        Ui.Modal.Name("well").Flyout().Open(_open).OnClose(() => _open = false)[
            _open
                ? Form.Model(_well).OnSubmit(_ => _open = false).ConfirmLeave("Mentetlen változások")[
                    _beats > 0 ? P["Someone else is editing this well."] : null,
                    Input.Bind(() => _well.Item).Id("item"),
                    Ui.Button.Id("save").Type(Ui.ButtonType.Submit)["Mentés"],
                    _change == SheetChange.Swapped
                        ? Ui.Button.Key("archive").OnClick(Archive)["Archiválás"]
                        : Ui.Button.Key(_change == SheetChange.Redrawn ? "remove-again" : "remove").Danger.OnClick(Remove)
                            .Confirm("Biztosan törli a kutat?")["Törlés"]
                ]
                : null
        ]
    ];

    private void Remove()
    {
        _deleted++;
        _open = false;
    }

    private void Archive() => _archived++;

    private void Beat(object? sender, SheetChange change)
    {
        _beats++;
        _change = change;
        StateHasChanged();
    }
}

/// <summary>The sheet with the kit's dialogs placed beside it as a layout would.</summary>
public sealed partial class ConfirmFlyoutDialogPage : Component
{
    protected override Component? HeadAssets => Markup.Title["confirm in a flyout"];

    protected override string? HtmlLang => "hu";

    protected override Component? Render() =>
    [
        ConfirmFlyoutSheet,
        Ui.ConfirmLeave.Heading("Mentetlen változások").Stay("Nem").Leave("Igen"),
        Ui.Confirm.Heading("Megerősítés").No("Nem").Yes("Igen")
    ];
}

/// <summary>The sheet alone: the browser asks.</summary>
public sealed partial class ConfirmFlyoutFallbackPage : Component
{
    protected override Component? HeadAssets => Markup.Title["confirm in a flyout, the browser asks"];

    protected override string? HtmlLang => "hu";

    protected override Component? Render() => ConfirmFlyoutSheet;
}
