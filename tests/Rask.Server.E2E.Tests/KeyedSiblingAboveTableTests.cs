using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A keyed bar of actions whose count text comes and goes above a kit table, ticked in a real browser (TA, first
///     seen on .246): the column headings stay on their columns, and only the count comes and goes.
/// </summary>
public sealed class KeyedSiblingAboveTableTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private static readonly string[] Headings = ["Dátum", "Mennyiség", "Sofőr"];

    [Fact]
    public async Task Ticking_and_unticking_rows_changes_only_the_count_and_never_the_column_headings()
    {
        SocketFrames? frames = null;
        await using var session = await HookSession.OpenAsync<TransfersPage>(
            playwright, beforeLoad: opened =>
            {
                frames = SocketFrames.Of(opened);
                return Task.CompletedTask;
            });
        var page = session.Page;
        await Expect(Headers(page)).ToHaveTextAsync(Headings);
        frames!.Clear();

        await Box(page, 1).ClickAsync();
        await Expect(page.Locator("[data-rask-key=count]")).ToHaveTextAsync("1 sor kijelölve");
        await Expect(Headers(page)).ToHaveTextAsync(Headings);
        await Box(page, 2).ClickAsync();
        await Expect(page.Locator("[data-rask-key=count]")).ToHaveTextAsync("2 sor kijelölve");
        await Box(page, 1).ClickAsync();
        await Box(page, 2).ClickAsync();
        await Expect(page.Locator("[data-rask-key=count]")).ToHaveCountAsync(0);
        await Expect(Headers(page)).ToHaveTextAsync(Headings);
        await Box(page, 0).ClickAsync();
        await page.Locator("[data-rask-key=none]").ClickAsync();

        await Expect(page.Locator("[data-rask-key=count]")).ToHaveCountAsync(0);
        await Expect(Headers(page)).ToHaveTextAsync(Headings);
        await Expect(page.Locator("tbody input:checked")).ToHaveCountAsync(0);
        Assert.True(frames.FullPages.Count == 0, "A tick was answered with the whole document:" + Environment.NewLine + frames.Trace());
        Assert.Empty(session.PageErrors);
    }

    private static ILocator Headers(IPage page) => page.Locator("thead th");

    private static ILocator Box(IPage page, int row) => page.Locator("tbody tr").Nth(row).Locator("input[type=checkbox]");
}

/// <summary>The bar and the table as the reporting app writes them.</summary>
public sealed partial class TransfersPage : Component
{
    private readonly HashSet<int> _chosen = [];

    protected override Component? HeadAssets => Markup.Title["keyed sibling above a table"];

    protected override string? HtmlLang => "hu";

    protected override Component? Render() =>
    [
        Div.Key("actions")[
            Ui.Button.Key("move").Disabled(_chosen.Count == 0)[$"Áthelyezés ({_chosen.Count})"],
            _chosen.Count > 0 ? Ui.Text.Key("count")[$"{_chosen.Count} sor kijelölve"] : null,
            Ui.Button.Key("none").Ghost.Sm.Disabled(_chosen.Count == 0).OnClick(_chosen.Clear)["Kijelölés törlése"]
        ],
        Ui.Table[
            Ui.TableColumns[Ui.TableColumn["Dátum"], Ui.TableColumn["Mennyiség"], Ui.TableColumn["Sofőr"]],
            Ui.TableRows[new[] { 11, 12, 13 }.Select(id => Ui.TableRow.Key(id)[
                Ui.TableCell[Ui.Checkbox.Checked(_chosen.Contains(id)).OnChange(on => Choose(id, on))],
                Ui.TableCell["2026-10-0" + (id - 10)],
                Ui.TableCell[(id * 10) + " l"],
                Ui.TableCell["Sofőr " + id]
            ])]
        ]
    ];

    private void Choose(int id, bool on)
    {
        if (on)
        {
            _chosen.Add(id);
        }
        else
        {
            _chosen.Remove(id);
        }
    }
}
