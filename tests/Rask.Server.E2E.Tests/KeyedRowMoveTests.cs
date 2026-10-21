using System.Globalization;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     Keyed rows moved past their neighbour by a button in the row, in a real browser (Eszaf, nightly .425): the
///     page shows the new order, and the next click acts on the row it was clicked in.
/// </summary>
public sealed class KeyedRowMoveTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    [Theory]
    [InlineData("#body")]
    [InlineData("#bare")]
    [InlineData("#kit")]
    public async Task Moving_a_keyed_row_up_twice_and_down_once_keeps_every_row_on_its_own_buttons(string table)
    {
        await using var session = await HookSession.OpenAsync<KeyedRowMovePage>(playwright);
        var page = session.Page;
        await Expect(Ids(page, table)).ToHaveTextAsync(["3", "7", "9"]);

        await Up(page, table, 1);
        await Expect(Ids(page, table)).ToHaveTextAsync(["7", "3", "9"]);
        await Up(page, table, 2);
        await Expect(Ids(page, table)).ToHaveTextAsync(["7", "9", "3"]);
        await Row(page, table, 0).Locator("[data-rask-key=down]").ClickAsync();
        await Expect(Ids(page, table)).ToHaveTextAsync(["9", "7", "3"]);
        await Row(page, table, 2).Locator("[data-rask-key=remove]").ClickAsync();
        await Expect(Ids(page, table)).ToHaveTextAsync(["9", "7"]);

        await Expect(page.Locator(table + "-said")).ToHaveTextAsync("9,7");
        Assert.Empty(session.PageErrors);
    }

    [Fact]
    public async Task Rows_that_arrive_in_an_empty_bare_table_land_in_the_body_the_browser_gives_them()
    {
        await using var session = await HookSession.OpenAsync<KeyedRowMovePage>(playwright);
        var page = session.Page;
        await Expect(page.Locator("#grow tr")).ToHaveCountAsync(0);

        await page.ClickAsync("#add");
        await page.ClickAsync("#add");
        await Expect(page.Locator("#grow > tbody > tr")).ToHaveCountAsync(2);
        await page.Locator("#grow tr").Nth(1).Locator("button").ClickAsync();

        await Expect(page.Locator("#grow > tbody > tr > td:first-child")).ToHaveTextAsync(["2", "1"]);
        Assert.Equal(0, await page.Locator("#grow > tr").CountAsync());
        Assert.Empty(session.PageErrors);
    }

    private static ILocator Ids(IPage page, string table) => page.Locator(table + " tr[data-rask-key] td:first-child");

    private static ILocator Row(IPage page, string table, int index) => page.Locator(table + " tr[data-rask-key]").Nth(index);

    private static Task Up(IPage page, string table, int row) => Row(page, table, row).Locator("[data-rask-key=up]").ClickAsync();
}

/// <summary>
///     The same three rows three times: plain elements under a <c>&lt;tbody&gt;</c>, plain rows straight under the
///     <c>&lt;table&gt;</c> (where the browser's parser adds a body the server never wrote), and the kit's rows; and a
///     bare table that starts with no rows at all.
/// </summary>
public sealed partial class KeyedRowMovePage : Component
{
    private readonly List<int> _body = [3, 7, 9];
    private readonly List<int> _bare = [3, 7, 9];
    private readonly List<int> _kit = [3, 7, 9];
    private readonly HashSet<int> _picked = [];
    private readonly List<int> _grow = [];

    protected override Component? HeadAssets => Markup.Title["keyed row move"];

    protected override string? HtmlLang => "en";

    protected override Component? Render() =>
        Div[
            Table.Id("body")[Tbody[_body.Select((id, i) => Tr.Key("r" + id)[Td[Text(id)], Td[Buttons(_body, i)]])]],
            P.Id("body-said")[string.Join(',', _body)],
            Table.Id("bare")[_bare.Select((id, i) => Tr.Key("r" + id)[Td[Text(id)], Td[Buttons(_bare, i)]])],
            P.Id("bare-said")[string.Join(',', _bare)],
            Div.Id("kit")[
                Ui.Table[
                    Ui.TableColumns[Ui.TableColumn["Id"], Ui.TableColumn["Pick"], Ui.TableColumn["Order"]],
                    Ui.TableRows[_kit.Select((id, i) => Ui.TableRow.Key("col-" + id)[
                        Ui.TableCell[Text(id)],
                        Ui.TableCell[Ui.Checkbox.Value(_picked.Contains(id)).OnChange(on => Pick(id, on))],
                        Ui.TableCell[
                            Ui.Button.Key("up").Ghost.Sm.OnClick(() => Swap(_kit, i, i - 1))["Up"],
                            Ui.Button.Key("down").Ghost.Sm.OnClick(() => Swap(_kit, i, i + 1))["Down"],
                            Ui.Button.Key("remove").Ghost.Sm.OnClick(() => _kit.RemoveAt(i))["Remove"]]])]]],
            P.Id("kit-said")[string.Join(',', _kit)],
            Button.Id("add").OnClick(() => _grow.Add(_grow.Count + 1))["add"],
            Table.Id("grow")[Caption["grows"], _grow.Select((id, i) => Tr.Key("g" + id)[Td[Text(id)], Td[Buttons(_grow, i)[0]]])]
        ];

    private static string Text(int id) => id.ToString(CultureInfo.InvariantCulture);

    private static Component[] Buttons(List<int> ids, int i) =>
    [
        Button.Key("up").OnClick(() => Swap(ids, i, i - 1))["Up"],
        Button.Key("down").OnClick(() => Swap(ids, i, i + 1))["Down"],
        Button.Key("remove").OnClick(() => ids.RemoveAt(i))["Remove"],
    ];

    private static void Swap(List<int> ids, int a, int b)
    {
        if (b >= 0 && b < ids.Count)
        {
            (ids[a], ids[b]) = (ids[b], ids[a]);
        }
    }

    private void Pick(int id, bool on)
    {
        if (on)
        {
            _picked.Add(id);
        }
        else
        {
            _picked.Remove(id);
        }
    }
}
