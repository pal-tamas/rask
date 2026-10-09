using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A click that leaves the browser before the page it was read from is replaced reaches its own handler or
///     nothing, in a real browser against a live server.
/// </summary>
/// <remarks>
///     <para>
///         The page is the shape this was found in: a kit table with a bin on every row, followed by a
///         confirmation that is always there. Removing a row moves every handler after it up by a row's worth,
///         so the id the last row's bin had belongs to the confirmation afterwards.
///     </para>
///     <para>
///         The window is held open on the server rather than timed: the handler that removes the row waits on a
///         gate the test owns, the second click is sent while it waits — so it provably carries the ids of the
///         page before the removal — and only then is the gate opened. The second click is dispatched from
///         script because the confirmation, a modal, covers the rows while it is open.
///     </para>
/// </remarks>
public sealed class StaleClickTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private static readonly LocatorAssertionsToHaveTextOptions Settled = new() { Timeout = 15_000 };

    [Fact]
    public async Task A_click_on_the_last_row_s_bin_sent_while_a_row_is_being_removed_asks_about_that_row()
    {
        var gate = LedgerJourneyPage.NewGate();
        await using var session = await HookSession.OpenAsync<LedgerJourneyPage>(playwright);
        var page = session.Page;
        await page.ClickAsync("#bin1");
        await Expect(page.Locator("#state")).ToHaveTextAsync("rows=1,2,3;removing=1;removed=;closed=0;", Settled);
        var idBefore = await page.GetAttributeAsync("#bin3", "data-rask-on-click");
        await page.ClickAsync("#yes");
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        await page.DispatchEventAsync("#bin3", "click");
        var stateWhenSent = await page.TextContentAsync("#state");
        gate.Open.SetResult();

        await Expect(page.Locator("#state")).ToHaveTextAsync("rows=2,3;removing=3;removed=1;closed=0;", Settled);
        Assert.Equal("rows=1,2,3;removing=1;removed=;closed=0;", stateWhenSent);
        Assert.NotEqual(idBefore, await page.GetAttributeAsync("#bin3", "data-rask-on-click"));
        Assert.Equal(idBefore, await page.GetAttributeAsync("#no", "data-rask-on-click"));
    }

    [Fact]
    public async Task A_click_on_the_bin_of_the_row_being_removed_removes_nothing_more()
    {
        var gate = LedgerJourneyPage.NewGate();
        await using var session = await HookSession.OpenAsync<LedgerJourneyPage>(playwright);
        var page = session.Page;
        await page.ClickAsync("#bin1");
        await Expect(page.Locator("#state")).ToHaveTextAsync("rows=1,2,3;removing=1;removed=;closed=0;", Settled);
        await page.ClickAsync("#yes");
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        await page.DispatchEventAsync("#bin1", "click");
        gate.Open.SetResult();

        // A click that follows is answered after the stale one: by then the stale one has done whatever it does.
        await Expect(page.Locator("#state")).ToHaveTextAsync("rows=2,3;removing=;removed=1;closed=0;", Settled);
        await page.ClickAsync("#bin2");
        await Expect(page.Locator("#state")).ToHaveTextAsync("rows=2,3;removing=2;removed=1;closed=0;", Settled);
    }

    [Fact]
    public async Task A_click_made_after_the_removal_was_shown_still_works_as_it_always_did()
    {
        var gate = LedgerJourneyPage.NewGate();
        gate.Open.SetResult();
        await using var session = await HookSession.OpenAsync<LedgerJourneyPage>(playwright);
        var page = session.Page;
        await page.ClickAsync("#bin1");
        await page.ClickAsync("#yes");
        await Expect(page.Locator("#state")).ToHaveTextAsync("rows=2,3;removing=;removed=1;closed=0;", Settled);

        await page.ClickAsync("#bin3");
        await Expect(page.Locator("#state")).ToHaveTextAsync("rows=2,3;removing=3;removed=1;closed=0;", Settled);
        await page.ClickAsync("#no");

        await Expect(page.Locator("#state")).ToHaveTextAsync("rows=2,3;removing=;removed=1;closed=1;", Settled);
    }
}

/// <summary>A kit table with a bin on every row, and the confirmation that follows it.</summary>
public sealed partial class LedgerJourneyPage : Component
{
    /// <summary>What a test holds the removal with: told when the handler has started, and what lets it finish.</summary>
    public sealed class RemovalGate
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Open { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record Row(int Id, string Name);

    // One page is open at a time: the class's tests run one after another, each with a gate of its own.
    private static RemovalGate _gate = new();

    private List<Row> _rows = [new(1, "Ada"), new(2, "Bob"), new(3, "Cyd")];
    private Row? _removing;
    private string _removed = "";
    private int _closed;

    public static RemovalGate NewGate() => _gate = new RemovalGate();

    protected override Component? HeadAssets => Markup.Title["ledger"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
    [
        P.Id("state")[$"rows={string.Join(',', _rows.Select(row => row.Id))};removing={_removing?.Id};removed={_removed};closed={_closed};"],
        Ui.Table[
            Ui.TableRows[
                _rows.Select(row => Ui.TableRow.Key(row.Id)[
                    Ui.TableCell[row.Name],
                    Ui.TableCell[A.Href($"/rows/{row.Id}")["edit"]],
                    Ui.TableCell[Ui.Button.Id($"bin{row.Id}").OnClick(() => _removing = row)["bin"]]
                ])
            ]
        ],
        Ui.Modal.Key("confirm-removal").Open(_removing is not null)[
            P[$"Remove {_removing?.Name}?"],
            Ui.Button.Id("no").OnClick(() => { _removing = null; _closed++; })["Nem"],
            Ui.Button.Danger.Id("yes").OnClick(Remove)["Igen"]
        ]
    ];

    private async Task Remove()
    {
        var gate = _gate;
        gate.Entered.TrySetResult();
        await gate.Open.Task;
        if (_removing is not { } row)
        {
            return;
        }

        _removed += row.Id;
        _rows = _rows.Where(other => other != row).ToList();
        _removing = null;
    }
}
