using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Cqrs;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A press on a header's notices bell — a keyed, tooltip'd square button in a child component — opens its flyout
///     while the page renders on its own every 100 ms: a pump's progress in a row, pushed by the server, in the
///     layout itself, in a table beside the header, or in the notices through an event they handle.
/// </summary>
/// <remarks>
///     Every row moves a handler, so every push is a new page. The race is forced with held replies, not timed: a
///     press read four pushed pages behind the server, a press made on the first response before any live page
///     landed, and a press made while the answer to the Escape that closed the flyout is still on its way.
/// </remarks>
public sealed class PressOnARenderingPageTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private static ILocator NoticesButton(IPage page) =>
        page.GetByRole(Microsoft.Playwright.AriaRole.Button, new() { Name = "Értesítés központ" });

    [Theory]
    [InlineData(PumpShape.Parent)]
    [InlineData(PumpShape.Sibling)]
    [InlineData(PumpShape.Itself)]
    public async Task A_press_sent_four_pushed_pages_behind_the_server_opens_the_flyout(PumpShape shape)
    {
        PumpScene.Shape = shape;
        await using var session = await OpenAsync(HeldReplies.Install);
        var page = session.Page;
        await page.WaitForTimeoutAsync(500);
        await HeldReplies.HoldAsync(page);
        await page.WaitForFunctionAsync("() => window.__held().length >= 4");

        await NoticesButton(page).ClickAsync();
        await page.WaitForTimeoutAsync(300);
        await HeldReplies.ReleaseAsync(page);

        await Expect(page.Locator("#opened")).ToHaveTextAsync("opened=1");
        await Expect(page.Locator("#notices-body")).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData(PumpShape.Parent, 0)]
    [InlineData(PumpShape.Itself, 3)]
    public async Task A_press_made_on_the_first_response_before_the_live_pages_land_opens_the_flyout(
        PumpShape shape, int behind)
    {
        PumpScene.Shape = shape;
        await using var session = await HookSession.OpenAsync<PumpLayoutPage>(playwright, beforeLoad: HoldFromTheStart, services: Cqrs);
        var page = session.Page;
        await page.WaitForFunctionAsync("n => window.__held().length >= n", behind);

        await NoticesButton(page).ClickAsync();
        await page.WaitForTimeoutAsync(300);
        await HeldReplies.ReleaseAsync(page);

        await Expect(page.Locator("#opened")).ToHaveTextAsync("opened=1");
        await Expect(page.Locator("#notices-body")).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData(PumpShape.Itself, false)]
    [InlineData(PumpShape.Itself, true)]
    public async Task The_bell_pressed_while_the_answer_to_escape_is_on_its_way_opens_the_notices(
        PumpShape shape, bool closeAnswerFirst)
    {
        PumpScene.Shape = shape;
        await using var session = await OpenAsync(HeldReplies.Install);
        var page = session.Page;
        await NoticesButton(page).ClickAsync();
        await Expect(page.Locator("dialog[open]")).ToContainTextAsync("chips sent");
        await HeldReplies.HoldAsync(page);

        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator("dialog[open]")).ToHaveCountAsync(0);
        if (closeAnswerFirst) await HeldReplies.UntilHeldAsync(page, "\"false\"");
        await NoticesButton(page).ClickAsync();
        await page.WaitForTimeoutAsync(400);
        await HeldReplies.ReleaseAsync(page);

        await Expect(page.Locator("#opened")).ToHaveTextAsync("opened=2");
        await Expect(page.Locator("dialog[open]")).ToContainTextAsync("chips sent", new() { Timeout = 5000 });
    }

    private static void Cqrs(IServiceCollection services) => services.AddRaskCqrs();

    private static async Task HoldFromTheStart(IPage page)
    {
        await HeldReplies.Install(page);
        await page.AddInitScriptAsync("window.__holding = true;");
    }

    private async Task<HookSession> OpenAsync(Func<IPage, Task>? beforeLoad)
    {
        var session = await HookSession.OpenAsync<PumpLayoutPage>(playwright, beforeLoad: beforeLoad, services: Cqrs);
        await session.HooksLoadedAsync();
        return session;
    }
}

/// <summary>Where the pump that renders on its own sits.</summary>
public enum PumpShape
{
    /// <summary>In the layout itself: the header's parent renders on every tick.</summary>
    Parent = 1,

    /// <summary>In a table beside the header: only it renders on every tick.</summary>
    Sibling,

    /// <summary>In the notices themselves: the bell's component handles an event published on every tick.</summary>
    Itself,
}

/// <summary>The shape the next page opens with; set by <see cref="PressOnARenderingPageTests" />, one test at a time.</summary>
public static class PumpScene
{
    private static volatile PumpShape _shape = PumpShape.Parent;

    public static PumpShape Shape
    {
        get => _shape;
        set => _shape = value;
    }
}

/// <summary>A pump sending, its progress pushed every 100 ms.</summary>
public sealed class PumpTicker : IDisposable
{
    private readonly Timer _timer;

    public PumpTicker(Action tick) => _timer = new Timer(_ => tick(), null, 100, 100);

    public void Dispose() => _timer.Dispose();
}

/// <summary>A layout: a header with the notices button, and the pump's table.</summary>
public sealed partial class PumpLayoutPage(IDispatcher dispatcher) : Component
{
    private PumpTicker? _ticker;
    private int _ticks;

    protected override Component? HeadAssets => Markup.Title["pump"];

    protected override string? HtmlLang => "hu";

    protected override Task OnMount()
    {
        _ticker = PumpScene.Shape switch
        {
            PumpShape.Parent => new PumpTicker(() =>
            {
                _ticks++;
                StateHasChanged();
            }),
            PumpShape.Itself => new PumpTicker(() => _ = dispatcher.Publish(new PumpHeard(Interlocked.Increment(ref _ticks)))),
            _ => null,
        };

        return Task.CompletedTask;
    }

    protected override Task OnUnmount()
    {
        _ticker?.Dispose();
        return Task.CompletedTask;
    }

    protected override Component? Render() =>
    [
        Header[PumpNotices],
        PumpScene.Shape == PumpShape.Sibling ? PumpTable : PumpRows(_ticks)
    ];

    /// <summary>The tick the last stop was pressed on.</summary>
    public static int Stopped { get; private set; }

    // A pump as a board reads it anew on every push: a new object every render, so the row's stop is a new handler.
    internal static Component PumpRows(int ticks)
    {
        var pump = new LivePump(ticks);
        return Ui.Table[
            Ui.TableRows[
                Ui.TableRow.Key("pump-1")[
                    Ui.TableCell[P.Id("ticks")[$"ticks={ticks}"]],
                    Ui.TableCell[Div.Style($"width:{ticks % 100}%")["sending"]],
                    Ui.TableCell[Ui.Button.Key("stop").OnClick(() => Stopped = pump.Ticks)["Leállítás"]]
                ]
            ]
        ];
    }
}

/// <summary>What the pump board heard: published on every tick of a sending.</summary>
public sealed record PumpHeard(int Number) : IEvent;

/// <summary>A pump's progress as read on one push.</summary>
public sealed record LivePump(int Ticks);

/// <summary>The pump's table, rendering on its own.</summary>
public sealed partial class PumpTable : Component
{
    private PumpTicker? _ticker;
    private int _ticks;

    protected override Task OnMount()
    {
        _ticker = new PumpTicker(() =>
        {
            _ticks++;
            StateHasChanged();
        });
        return Task.CompletedTask;
    }

    protected override Task OnUnmount()
    {
        _ticker?.Dispose();
        return Task.CompletedTask;
    }

    protected override Component? Render() => PumpLayoutPage.PumpRows(_ticks);
}

/// <summary>
///     The header's notices, as a notification centre draws them: a tooltip'd square bell opening a flyout that
///     lists the last word on each pump, drawn again on every push of its own.
/// </summary>
public sealed partial class PumpNotices : Component, IEventHandler<PumpHeard>
{
    private bool _open;
    private int _opened;
    private int _heard;

    // What the board heard is drawn again whenever it changes.
    public Task Handle(PumpHeard e)
    {
        _heard = e.Number;
        return Task.CompletedTask;
    }

    protected override Component? Render() =>
    [
        P.Id("opened")[$"opened={_opened}"],
        Ui.Button.Key("notices").Ghost.Square(true).Icon(Ui.IconName.Bell).Tooltip("Értesítés központ")
            .OnClick(() =>
            {
                _open = true;
                _opened++;
            }),
        Ui.Modal.Key("notice-centre").Flyout(true).Open(_open).OnClose(() => { _open = false; })[
            Div.Id("notices-body").Class("min-w-[22rem] space-y-6")[
                Ui.Heading.Level(2).Lg["Értesítés központ"],
                Div.Key("list")[
                    Enumerable.Range(0, 3).Select(pump =>
                        Div.Key($"pump-{pump}").Class("py-3")[$"pump {pump}: {_heard + pump} chips sent"])
                ]
            ]
        ]
    ];
}
