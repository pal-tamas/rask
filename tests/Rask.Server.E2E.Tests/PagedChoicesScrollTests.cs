using System.Globalization;
using Microsoft.Playwright;
using Rask.Core;
using Rask.Server.E2E.Tests.Infrastructure;
using Rask.Site.E2E.Tests.Infrastructure;
using static Microsoft.Playwright.Assertions;

#pragma warning disable RASK019 // a small test page; its <head> is not what is under test

namespace Rask.Server.E2E.Tests;

/// <summary>
///     A pillbox over 500 searched products that pages as its list scrolls (<c>.Choices((typed, page) =&gt; …)</c>, #1354),
///     in a real browser over the real socket, at a desktop's width and a phone's: the wheel and the End key reach the last
///     product, a search narrows the list and scrolling loads the rest of what it found, and a product of the third page is
///     picked. Eszaf's product picker showed its first twenty and could not be scrolled past them.
/// </summary>
public sealed class PagedChoicesScrollTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
{
    private const string Opener = "#products-at [data-ui-pillbox-trigger]";

    private const string Box = "#products-at [data-rask-list-window]";

    private const string Search = "#products-at [data-ui-pillbox-search] input";

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
    public async Task Scrolling_alone_loads_page_after_page_to_the_last_product_and_picks_it(string how, int width)
    {
        await using var session = await OpenAsync(width);
        var page = session.Page;
        var box = await OpenListAsync(session);

        await ToTheRowAsync(page, box, PagedChoicesPage.NameOf(500), how);
        await box.Locator("[title='" + PagedChoicesPage.NameOf(500) + "']").ClickAsync();

        await Expect(page.Locator("#picked")).ToHaveTextAsync("500");
        Assert.Equal("500", await box.Locator("[role=option]").Last.GetAttributeAsync("aria-setsize"));
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public async Task A_search_narrows_the_list_and_scrolling_loads_the_rest_of_what_it_found(int width)
    {
        await using var session = await OpenAsync(width);
        var page = session.Page;
        var box = await OpenListAsync(session);
        await page.Locator(Search).FillAsync("product 4");
        await Expect(box.Locator("[title='" + PagedChoicesPage.NameOf(400) + "']")).ToBeVisibleAsync();

        await ToTheRowAsync(page, box, PagedChoicesPage.NameOf(499), "wheel");

        Assert.EndsWith(" 100", await box.GetAttributeAsync("data-rask-list-window"), StringComparison.Ordinal);
        await Expect(box.Locator("[title='" + PagedChoicesPage.NameOf(399) + "']")).ToHaveCountAsync(0);
        Assert.Equal("100", await box.Locator("[role=option]").Last.GetAttributeAsync("aria-setsize"));
    }

    [Theory]
    [MemberData(nameof(Widths))]
    public async Task A_product_of_the_third_page_is_picked(int width)
    {
        await using var session = await OpenAsync(width);
        var page = session.Page;
        var box = await OpenListAsync(session);

        await ToTheRowAsync(page, box, PagedChoicesPage.NameOf(125), "wheel");
        await box.Locator("[title='" + PagedChoicesPage.NameOf(125) + "']").ClickAsync();

        await Expect(page.Locator("#picked")).ToHaveTextAsync("125");
        await Expect(page.Locator(Opener)).ToContainTextAsync(PagedChoicesPage.NameOf(125));
    }

    private Task<HookSession> OpenAsync(int width) =>
        HookSession.OpenAsync<PagedChoicesPage>(
            playwright, new BrowserNewContextOptions { ViewportSize = new() { Width = width, Height = 844 } });

    private static async Task<ILocator> OpenListAsync(HookSession session)
    {
        var page = session.Page;
        await session.HooksLoadedAsync();
        await page.Locator(Opener).ClickAsync();
        var box = page.Locator(Box);
        await Expect(box).ToBeVisibleAsync();
        await Expect(box.Locator("[title='" + PagedChoicesPage.NameOf(1) + "']")).ToBeVisibleAsync();
        return box;
    }

    // Scrolls the way a reader does until the row is in the box: each step that reaches the end of the rows here loads the
    // next page, and the box goes on from there.
    private static async Task ToTheRowAsync(IPage page, ILocator box, string name, string how)
    {
        var row = box.Locator("[title='" + name + "']");
        if (how == "wheel")
        {
            await box.HoverAsync();
        }

        for (var i = 0; i < 400 && !await InSightAsync(box, row); i++)
        {
            if (how == "wheel")
            {
                // Less than the box is tall, so no row is scrolled past unseen.
                await page.Mouse.WheelAsync(0, 200);
            }
            else
            {
                await page.Keyboard.PressAsync("End");
            }

            await page.WaitForTimeoutAsync(60);
        }

        Assert.True(await InSightAsync(box, row), name + " was never scrolled into the box");
    }

    private static async Task<bool> InSightAsync(ILocator box, ILocator row) =>
        await row.CountAsync() == 1 && await row.EvaluateAsync<bool>(
            """
            (row) => {
                const box = row.closest('[data-rask-list-window]'), at = row.getBoundingClientRect(), b = box.getBoundingClientRect();
                return at.top >= b.top - 1 && at.bottom <= b.bottom + 1;
            }
            """);
}

/// <summary>A pillbox over 500 products, searched and paged by the store, as Eszaf's product picker reads its table.</summary>
public sealed partial class PagedChoicesPage : Component
{
    private const int Products = 500;

    private ICollection<int> _picked = [];

    internal static string NameOf(int id) => "Product " + id.ToString("000", CultureInfo.InvariantCulture);

    // The store: the products whose name holds what was typed, in the order of their ids, a page of them.
    private static Task<IReadOnlyList<Ui.Choice<int>>> Find(string typed, Ui.ChoicePage page) =>
        Task.FromResult<IReadOnlyList<Ui.Choice<int>>>(
            [
                .. Enumerable.Range(1, Products)
                    .Where(id => NameOf(id).Contains(typed, StringComparison.OrdinalIgnoreCase))
                    .Skip(page.Skip).Take(page.Take)
                    .Select(id => new Ui.Choice<int>(id, NameOf(id))),
            ]);

    protected override Component? HeadAssets => [Markup.Title["paged choices"], Markup.Style[Raw.Value(UiStylesheet.Css)]];

    protected override Component? Render() => Main.Class("p-4 space-y-4").Attributes([(UiStylesheet.DocumentAttribute, "")])[
        Div.Id("products-at")[
            Ui.Pillbox.Values(_picked).OnChange(values => _picked = values).Choices(Find)
        ],
        P.Id("picked")[string.Join(",", _picked)]
    ];
}
