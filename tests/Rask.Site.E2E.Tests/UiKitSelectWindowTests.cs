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
