using Microsoft.Playwright;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

namespace Rask.Site.E2E.Tests;

/// <summary>
///     The pillbox page's 500 products that page as the list scrolls (<c>.Choices((typed, page) =&gt; …)</c>, #1354), in the
///     browser-WASM site at a desktop's width and a phone's: the wheel and the End key reach the last product, a search
///     narrows the list and scrolling loads the rest of what it found, and a product of the third page is picked.
/// </summary>
public sealed class UiKitPillboxPagedTests(WasmExampleAppFixture app, PlaywrightFixture pw)
    : SharedSmokeTests(pw)
{
    protected override string BaseUrl => app.BaseUrl;
    protected override string FixtureName => "Wasm";
    protected override string ServerLog => app.ServerLog;

    private ILocator Scope => Page.Locator("[data-testid='ui-pillbox-paged']");

    private ILocator Box => Scope.Locator("[data-rask-list-window]");

    private ILocator State => Page.Locator("[data-testid='ui-pillbox-paged-state']");

    private static string NameOf(int id) => "Product " + id.ToString("000", System.Globalization.CultureInfo.InvariantCulture);

    public static TheoryData<string, int> Ways()
    {
        var data = new TheoryData<string, int>();
        foreach (var how in new[] { "wheel", "end" })
        {
            data.Add(how, 1280);
            data.Add(how, 390);
        }

        return data;
    }

    public static TheoryData<int> Widths() => [1280, 390];

    [Theory]
    [MemberData(nameof(Ways))]
    public Task Scrolling_alone_loads_page_after_page_to_the_last_product_and_picks_it(string how, int width) => RunAsync(async () =>
    {
        await OpenAsync(width);

        await ToTheRowAsync(NameOf(500), how);
        await Box.Locator("[title='" + NameOf(500) + "']").ClickAsync();

        await Expect(State).ToHaveTextAsync("Products: 500.");
        Assert.Equal("500", await Box.Locator("[role=option]").Last.GetAttributeAsync("aria-setsize"));
    });

    [Theory]
    [MemberData(nameof(Widths))]
    public Task A_search_narrows_the_list_and_scrolling_loads_the_rest_of_what_it_found(int width) => RunAsync(async () =>
    {
        await OpenAsync(width);
        await Scope.Locator("[data-ui-pillbox-search] input").FillAsync("product 4");
        await Expect(Box.Locator("[title='" + NameOf(400) + "']")).ToBeVisibleAsync();

        await ToTheRowAsync(NameOf(499), "wheel");

        Assert.EndsWith(" 100", await Box.GetAttributeAsync("data-rask-list-window"), StringComparison.Ordinal);
        Assert.Equal("100", await Box.Locator("[role=option]").Last.GetAttributeAsync("aria-setsize"));
    });

    [Theory]
    [MemberData(nameof(Widths))]
    public Task A_product_of_the_third_page_is_picked(int width) => RunAsync(async () =>
    {
        await OpenAsync(width);

        await ToTheRowAsync(NameOf(125), "wheel");
        await Box.Locator("[title='" + NameOf(125) + "']").ClickAsync();

        await Expect(State).ToHaveTextAsync("Products: 125.");
        await Expect(Scope.Locator("[data-ui-pillbox-trigger]")).ToContainTextAsync(NameOf(125));
    });

    private async Task OpenAsync(int width)
    {
        await Page.SetViewportSizeAsync(width, 844);
        await OpenUiPageAsync("pillbox", "Pillbox");
        await Page.Locator("#ui-pillbox-paged").ClickAsync();
        await Expect(Box).ToBeVisibleAsync();
        await Expect(Box.Locator("[title='" + NameOf(1) + "']")).ToBeVisibleAsync();
    }

    // Scrolls the way a reader does until the row is in the box: a step that reaches the end of the rows here loads the
    // next page, and the box goes on from there.
    private async Task ToTheRowAsync(string name, string how)
    {
        var row = Box.Locator("[title='" + name + "']");
        if (how == "wheel")
        {
            await Box.HoverAsync();
        }

        for (var i = 0; i < 400 && !await InSightAsync(row); i++)
        {
            if (how == "wheel")
            {
                // Less than the box is tall, so no row is scrolled past unseen.
                await Page.Mouse.WheelAsync(0, 200);
            }
            else
            {
                await Page.Keyboard.PressAsync("End");
            }

            await Page.WaitForTimeoutAsync(60);
        }

        Assert.True(await InSightAsync(row), name + " was never scrolled into the box");
    }

    private static async Task<bool> InSightAsync(ILocator row) =>
        await row.CountAsync() == 1 && await row.EvaluateAsync<bool>(
            """
            (row) => {
                const box = row.closest('[data-rask-list-window]'), at = row.getBoundingClientRect(), b = box.getBoundingClientRect();
                return at.top >= b.top - 1 && at.bottom <= b.bottom + 1;
            }
            """);
}
