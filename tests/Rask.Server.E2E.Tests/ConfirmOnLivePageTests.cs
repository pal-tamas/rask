using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A <c>.Confirm("…")</c> delete on a page that renders again every 200 ms the whole time the reader is asked —
///     a board told of every change, its rows read anew, so the delete's handler is a new closure in every render
///     and its id moves as a control before it comes and goes. Yes still runs the delete the reader pressed, exactly
///     once, however many pages were drawn while the question was open; no, a delete gone, or another action drawn
///     where it was runs nothing. Asked in <c>Ui.Confirm</c> and in the browser's own dialog.
/// </summary>
public sealed class ConfirmOnLivePageTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Dialog = "#ui-confirm";
    private const string Yes = Dialog + " [data-rask-confirm-dialog=go]";
    private const string No = Dialog + " .flex button:not([data-rask-confirm-dialog])";
    private const string Delete = "button[data-rask-confirm]";

    // Long enough for ten renders: far past the two pages an event outlives a render by.
    private const int Asked = 2000;

    [Theory]
    [InlineData(LiveScene.Delete, true, 1)]
    [InlineData(LiveScene.Delete, false, 0)]
    [InlineData(LiveScene.Gone, true, 0)]
    [InlineData(LiveScene.Archive, true, 0)]
    public async Task The_kit_dialog_runs_the_pressed_delete_only_on_yes(LiveScene meanwhile, bool yes, int deleted)
    {
        await using var session = await OpenAsync<ConfirmLiveDialogPage>();
        var page = session.Page;
        await OpenFlyoutAsync(page);

        await page.ClickAsync(Delete);
        await Expect(page.Locator(Dialog)).ToBeVisibleAsync();
        ConfirmLiveScene.Now = meanwhile;
        await RendersAsync(page);
        await page.ClickAsync(yes ? Yes : No);
        await Expect(page.Locator(Dialog)).ToBeHiddenAsync();

        await SettledAsync(page, deleted);
        Assert.Empty(session.PageErrors);
    }

    [Theory]
    [InlineData(LiveScene.Delete, true, 1)]
    [InlineData(LiveScene.Delete, false, 0)]
    [InlineData(LiveScene.Gone, true, 0)]
    [InlineData(LiveScene.Archive, true, 0)]
    public async Task The_browsers_own_dialog_runs_the_pressed_delete_only_on_yes(
        LiveScene meanwhile, bool yes, int deleted)
    {
        await using var session = await OpenAsync<ConfirmLiveFallbackPage>();
        var page = session.Page;
        await OpenFlyoutAsync(page);
        var asked = 0;
        page.Dialog += (_, dialog) =>
        {
            asked++;
            _ = AnswerAsync(dialog, meanwhile, yes);
        };

        await page.ClickAsync(Delete);
        await page.WaitForTimeoutAsync(Asked + 300);

        await SettledAsync(page, deleted);
        Assert.Equal(1, asked);
    }

    // The server renders on while the browser's dialog holds the page still.
    private static async Task AnswerAsync(IDialog dialog, LiveScene meanwhile, bool yes)
    {
        ConfirmLiveScene.Now = meanwhile;
        await Task.Delay(Asked);
        await (yes ? dialog.AcceptAsync() : dialog.DismissAsync());
    }

    // Ten renders or so land while the question is open.
    private static async Task RendersAsync(IPage page)
    {
        var before = await TicksAsync(page);
        await page.WaitForTimeoutAsync(Asked);
        Assert.True(await TicksAsync(page) >= before + 5, "the page went on rendering while the reader was asked");
    }

    private static async Task<int> TicksAsync(IPage page) =>
        int.Parse((await page.Locator("#ticks").TextContentAsync())!["ticks=".Length..],
            System.Globalization.CultureInfo.InvariantCulture);

    private static async Task SettledAsync(IPage page, int deleted)
    {
        await Expect(page.Locator("#deleted")).ToHaveTextAsync($"deleted={deleted}");
        await page.WaitForTimeoutAsync(500);
        await Expect(page.Locator("#deleted")).ToHaveTextAsync($"deleted={deleted}");
        await Expect(page.Locator("#archived")).ToHaveTextAsync("archived=0");
    }

    private static async Task OpenFlyoutAsync(IPage page)
    {
        ConfirmLiveScene.Now = LiveScene.Delete;
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

/// <summary>What the live sheet draws where the delete is.</summary>
public enum LiveScene
{
    /// <summary>The delete, asking first.</summary>
    Delete = 1,

    /// <summary>Nothing: the well was deleted elsewhere.</summary>
    Gone,

    /// <summary>Another action, under the delete's own key, asking nothing.</summary>
    Archive,
}

/// <summary>What the open sheet draws from its next render on; set by <see cref="ConfirmOnLivePageTests" />, one test at a time.</summary>
public static class ConfirmLiveScene
{
    private static volatile LiveScene _now = LiveScene.Delete;

    public static LiveScene Now
    {
        get => _now;
        set => _now = value;
    }
}

/// <summary>A row as a board reads it anew on every change: a new object every render.</summary>
public sealed record LiveWell(int Read);

/// <summary>An edit sheet on a board that renders again every 200 ms, its rows read anew each time.</summary>
public sealed partial class ConfirmLiveSheet : Component
{
    private Timer? _timer;
    private bool _open;
    private int _ticks;
    private int _deleted;
    private int _archived;
    private int _refreshed;

    protected override Task OnMount()
    {
        _timer = new Timer(_ => Tick(), null, 200, 200);
        return Task.CompletedTask;
    }

    protected override async Task OnUnmount()
    {
        if (_timer is { } timer) await timer.DisposeAsync();
    }

    protected override Component? Render()
    {
        var well = new LiveWell(_ticks);
        return
        [
            P.Id("deleted")[$"deleted={_deleted}"],
            P.Id("archived")[$"archived={_archived}"],
            P.Id("ticks")[$"ticks={_ticks}"],
            Button.Id("open").OnClick(() => _open = true)["open"],
            Ui.Modal.Name("well").Flyout().Open(_open).OnClose(() => _open = false)[
                _open
                    ? Div[
                        // A control that comes and goes before the delete: the delete's handler id moves with it.
                        _ticks % 2 == 1 ? Button.OnClick(() => _refreshed += well.Read)["Frissítés"] : null,
                        ConfirmLiveScene.Now switch
                        {
                            LiveScene.Gone => null,
                            LiveScene.Archive => Ui.Button.Key("remove").OnClick(() => Archive(well))["Archiválás"],
                            _ => Ui.Button.Key("remove").Danger.OnClick(() => Remove(well))
                                .Confirm("Biztosan törli a kutat?")["Törlés"],
                        }
                    ]
                    : null
            ]
        ];
    }

    private void Remove(LiveWell well)
    {
        _deleted += well.Read >= 0 ? 1 : 0;
        _open = false;
    }

    private void Archive(LiveWell well) => _archived += well.Read >= 0 ? 1 : 0;

    private void Tick()
    {
        _ticks++;
        StateHasChanged();
    }
}

/// <summary>The live sheet with the kit's dialog placed beside it as a layout would.</summary>
public sealed partial class ConfirmLiveDialogPage : Component
{
    protected override Component? HeadAssets => Markup.Title["confirm on a live page"];

    protected override string? HtmlLang => "hu";

    protected override Component? Render() =>
    [
        ConfirmLiveSheet,
        Ui.Confirm.Heading("Megerősítés").No("Nem").Yes("Igen")
    ];
}

/// <summary>The live sheet alone: the browser asks.</summary>
public sealed partial class ConfirmLiveFallbackPage : Component
{
    protected override Component? HeadAssets => Markup.Title["confirm on a live page, the browser asks"];

    protected override string? HtmlLang => "hu";

    protected override Component? Render() => ConfirmLiveSheet;
}
