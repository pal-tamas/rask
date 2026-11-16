using System.Globalization;
using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     A select of 1200 options in a browser: only the rows in view are on the page, and the scroll, the search, the
///     arrow keys, Home and End and type-ahead reach every one of the rest.
/// </summary>
public sealed class UiKitSelectWindowTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Wasm";
    protected override string ServerLog => app.ServerLog;

    private ILocator Select(string id) => Page.Locator("[data-ui-select]", new PageLocatorOptions { Has = Page.Locator("#" + id) });

    private ILocator State => Page.Locator("[data-testid='ui-select-long-state']");

    // The row the cursor is on, as a screen reader is told it.
    private Task<string> LitAsync(string id) =>
        Page.EvaluateAsync<string>(
            """
            id => {
                const root = document.getElementById(id).closest('[data-ui-select]');
                const named = root.querySelector('[aria-activedescendant]');
                const row = named && document.getElementById(named.getAttribute('aria-activedescendant'));
                return row ? row.textContent.trim() : '';
            }
            """, id);

    // The cursor lands once the page has drawn its row: asked again until it has, for as long as an assertion waits.
    private async Task LitIsAsync(string id, string expected)
    {
        var lit = "";
        for (var i = 0; i < 100 && lit != expected; i++)
        {
            lit = await LitAsync(id);
            if (lit != expected)
            {
                await Page.WaitForTimeoutAsync(100);
            }
        }

        Assert.Equal(expected, lit);
    }

    [Fact]
    public Task A_long_list_is_scrolled_to_its_end_without_typing_and_its_last_row_picked() => RunAsync(async () =>
    {
        await OpenUiPageAsync("select", "Select");
        var list = Select("ui-select-long-list");
        await Page.Locator("#ui-select-long-list").ClickAsync();
        var box = list.Locator("[data-rask-list-window]");
        await Expect(box).ToBeVisibleAsync();

        var drawn = await list.Locator("[role='option']").CountAsync();
        await box.EvaluateAsync("box => box.scrollTop = box.scrollHeight");
        var last = list.Locator("[role='option']", new LocatorLocatorOptions { HasTextString = "Volvo 1200" });
        await Expect(last).ToBeVisibleAsync();
        await last.ClickAsync();

        Assert.InRange(drawn, 1, 40);
        Assert.Equal("1200", await last.GetAttributeAsync("aria-setsize"));
        await Expect(State).ToContainTextAsync("Vehicle: 1200.");
        await Expect(Page.Locator("#ui-select-long-list")).ToContainTextAsync("Volvo 1200");
    });

    public static TheoryData<string, string, int> LongNames()
    {
        var data = new TheoryData<string, string, int>();
        foreach (var list in new[] { "ui-select-long-details", "ui-select-long-codes" })
        {
            foreach (var how in new[] { "wheel", "scrollbar", "end" })
            {
                data.Add(list, how, 1280);
                data.Add(list, how, 390);
            }
        }

        return data;
    }

    // The two pickers TA and Eszaf draw: 1100 vehicles of which a few say a detail and some names are far too long for a
    // row, and 480 activity codes of long names and no detail. Scrolled and nothing else, the last option is reached, in
    // view with no blank room under it, every row exactly as tall as the window counts it, and it is the one picked.
    [Theory]
    [MemberData(nameof(LongNames))]
    public Task Scrolling_alone_reaches_the_last_of_a_long_list_of_long_names_and_picks_it(string id, string how, int width) => RunAsync(async () =>
    {
        await Page.SetViewportSizeAsync(width, 844);
        await OpenUiPageAsync("select", "Select");
        var list = Select(id);
        await Page.Locator("#" + id).ClickAsync();
        var box = list.Locator("[data-rask-list-window]");
        await Expect(box).ToBeVisibleAsync();
        var (count, field) = id == "ui-select-long-details" ? (1100, "Vehicle") : (480, "Activity");

        await ScrollToTheEndAsync(box, how);
        var settled = await SettledAsync(box, count);
        await box.Locator("[aria-posinset='" + count.ToString(CultureInfo.InvariantCulture) + "']").ClickAsync();

        Assert.InRange(settled.Gap, 0, 6);
        Assert.True(settled.Under >= 0, "the last option is cut off at the top of the list");
        Assert.Equal(settled.Counted, settled.Heights);
        await Expect(Page.Locator("[data-testid='ui-select-long-names-state']")).ToContainTextAsync(field + ": " + count.ToString(CultureInfo.InvariantCulture) + ".");
    });

    private async Task ScrollToTheEndAsync(ILocator box, string how)
    {
        if (how == "end")
        {
            await Page.Keyboard.PressAsync("End");
            return;
        }

        if (how == "scrollbar")
        {
            // Where the scrollbar is drawn over the content (headless, macOS) there is no thumb to drag: the box is put
            // where a drag to the very bottom leaves it.
            await box.EvaluateAsync("box => box.scrollTop = box.scrollHeight");
            return;
        }

        await box.HoverAsync();
        for (var i = 0; i < 400 && !await box.EvaluateAsync<bool>("box => box.scrollTop + box.clientHeight >= box.scrollHeight - 1"); i++)
        {
            await Page.Mouse.WheelAsync(0, 1500);
            await Page.WaitForTimeoutAsync(30);
        }
    }

    // Once the page has drawn the rows the box is scrolled to: the room under the row at place `count`, how far under the
    // box's top it starts, the heights of every row drawn, and the height the window counts a row at.
    private static async Task<(int Gap, int Under, string Heights, string Counted)> SettledAsync(ILocator box, int count)
    {
        var said = string.Empty;
        var same = 0;
        for (var i = 0; i < 100 && same < 5; i++)
        {
            var now = await box.EvaluateAsync<string>(
                """
                (box, count) => {
                    const rows = [...box.querySelectorAll('[role=option]')];
                    const heights = [...new Set(rows.map(r => Math.round(r.getBoundingClientRect().height)))].join(',');
                    const counted = box.getAttribute('data-rask-list-window').split(' ')[0];
                    const row = rows.find(r => r.getAttribute('aria-posinset') === String(count));
                    if (!row) return ['999999', '0', heights, counted].join('|');
                    const top = box.getBoundingClientRect().top + box.clientTop, at = row.getBoundingClientRect();
                    return [Math.round(top + box.clientHeight - at.bottom), Math.round(at.top - top), heights, counted].join('|');
                }
                """,
                count);
            same = string.Equals(now, said, StringComparison.Ordinal) ? same + 1 : 0;
            said = now;
            await box.Page.WaitForTimeoutAsync(60);
        }

        var parts = said.Split('|');
        return (int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture), parts[2], parts[3]);
    }

    [Fact]
    public Task The_search_narrows_all_1200_rows_not_the_rows_drawn() => RunAsync(async () =>
    {
        await OpenUiPageAsync("select", "Select");
        var list = Select("ui-select-long-list");
        await Page.Locator("#ui-select-long-list").ClickAsync();

        await list.Locator("[data-ui-select-search] input").FillAsync("1199");
        var rows = list.Locator("[role='option']");
        await Expect(rows).ToHaveCountAsync(1);
        await rows.First.ClickAsync();

        await Expect(State).ToContainTextAsync("Vehicle: 1199.");
    });

    [Fact]
    public Task The_arrow_keys_End_and_Home_reach_rows_past_the_ones_drawn() => RunAsync(async () =>
    {
        await OpenUiPageAsync("select", "Select");
        await Page.Locator("#ui-select-long-plain").ClickAsync();
        await Expect(Select("ui-select-long-plain").Locator("[data-rask-list-window]")).ToBeVisibleAsync();

        for (var i = 0; i < 30; i++)
        {
            await Page.Keyboard.PressAsync("ArrowDown");
        }

        await LitIsAsync("ui-select-long-plain", "DAF 0031");
        await Page.Keyboard.PressAsync("End");
        await LitIsAsync("ui-select-long-plain", "Volvo 1200");
        await Page.Keyboard.PressAsync("ArrowUp");
        await LitIsAsync("ui-select-long-plain", "Scania 1199");
        await Page.Keyboard.PressAsync("Home");
        await LitIsAsync("ui-select-long-plain", "DAF 0001");
        await Page.Keyboard.PressAsync("End");
        await LitIsAsync("ui-select-long-plain", "Volvo 1200");
        await Page.Keyboard.PressAsync("Enter");

        await Expect(State).ToContainTextAsync("Trailer: 1200.");
    });

    [Fact]
    public Task Type_ahead_lands_on_a_row_past_the_ones_drawn() => RunAsync(async () =>
    {
        await OpenUiPageAsync("select", "Select");
        await Page.Locator("#ui-select-long-plain").ClickAsync();
        await Expect(Select("ui-select-long-plain").Locator("[data-rask-list-window]")).ToBeVisibleAsync();

        foreach (var lands in new[] { "Volvo 0006", "Volvo 0012", "Volvo 0018", "Volvo 0024", "Volvo 0030" })
        {
            await Page.Keyboard.PressAsync("v");
            await LitIsAsync("ui-select-long-plain", lands);
        }

        await Page.Keyboard.PressAsync("Enter");
        await Expect(State).ToContainTextAsync("Trailer: 30.");
    });

    [Fact]
    public Task A_long_list_opens_on_its_picked_row() => RunAsync(async () =>
    {
        await OpenUiPageAsync("select", "Select");
        var button = Page.Locator("#ui-select-long-list");
        var list = Select("ui-select-long-list");
        await button.ClickAsync();
        await list.Locator("[data-ui-select-search] input").FillAsync("0950");
        await list.Locator("[role='option']").First.ClickAsync();
        await Expect(State).ToContainTextAsync("Vehicle: 950.");

        await button.ClickAsync();

        await LitIsAsync("ui-select-long-list", "Iveco 0950");
        await Expect(list.Locator("[role='option'][aria-selected='true']")).ToBeInViewportAsync();
    });
}
