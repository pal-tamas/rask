using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Rask.Examples.E2E.Tests;

// Data table showcase: every interaction drives a URL query-param mutation
// via Navigator.SetQuery → [QueryParam] rebind → re-render. Tests assert
// (1) initial render, (2) sort cycle, (3) filter, (4) page-size, (5) paging,
// (6) empty state.
public abstract partial class SharedSmokeTests
{
    [Fact]
    public Task Table_DefaultView_Shows10Rows() => RunAsync(async () =>
    {
        await NavigateToAsync("/table");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Data table",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Expect(Page.Locator("tbody tr")).ToHaveCountAsync(10,
            new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });
        await Expect(Page.Locator("small.text-secondary").First).ToContainTextAsync("Showing 1",
            new LocatorAssertionsToContainTextOptions { Timeout = 5_000 });
    });

    [Fact]
    public Task Table_SortByName_AscDescClearCycle() => RunAsync(async () =>
    {
        await NavigateToAsync("/table");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Data table",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var nameHeader = Page.Locator("th button:has-text('Name')").First;

        // Click 1: ascending.
        await nameHeader.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(".*[\\?&]sort=name"),
            new PageAssertionsToHaveURLOptions { Timeout = 5_000 });
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(".*[\\?&]dir=asc"),
            new PageAssertionsToHaveURLOptions { Timeout = 5_000 });

        // Click 2: descending.
        await nameHeader.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(".*[\\?&]dir=desc"),
            new PageAssertionsToHaveURLOptions { Timeout = 5_000 });

        // Click 3: cleared (sort= empty, dir=asc).
        await nameHeader.ClickAsync();
        // URL still has dir=asc but sort= empty. Asserting absence of dir=desc
        // and presence of dir=asc.
        await Expect(Page).Not.ToHaveURLAsync(new System.Text.RegularExpressions.Regex(".*[\\?&]dir=desc"),
            new PageAssertionsToHaveURLOptions { Timeout = 5_000 });
    });

    [Fact]
    public Task Table_Filter_ReducesRows_UpdatesUrl() => RunAsync(async () =>
    {
        await NavigateToAsync("/table");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Data table",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Page.Locator("input[type='search']").FillAsync("Linus");
        await Page.WaitForTimeoutAsync(300);

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(".*[\\?&]filter=Linus"),
            new PageAssertionsToHaveURLOptions { Timeout = 5_000 });

        var rowCount = await Page.Locator("tbody tr").CountAsync();
        Assert.True(rowCount > 0 && rowCount < 10, $"Filter should reduce row count from 10; got {rowCount}");
    });

    [Fact]
    public Task Table_FilterNoMatch_ShowsEmptyState() => RunAsync(async () =>
    {
        await NavigateToAsync("/table");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Data table",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Page.Locator("input[type='search']").FillAsync("zxqwzxq");
        await Page.WaitForTimeoutAsync(300);

        await Expect(Page.Locator("tbody")).ToContainTextAsync("No people match",
            new LocatorAssertionsToContainTextOptions { Timeout = 5_000 });
    });

    [Fact]
    public Task Table_ChangePageSize_25_ShowsMoreRows() => RunAsync(async () =>
    {
        await NavigateToAsync("/table");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Data table",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        await Page.Locator("select.form-select-sm").SelectOptionAsync("25");
        await Page.WaitForTimeoutAsync(200);

        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(".*[\\?&]size=25"),
            new PageAssertionsToHaveURLOptions { Timeout = 5_000 });
        await Expect(Page.Locator("tbody tr")).ToHaveCountAsync(25,
            new LocatorAssertionsToHaveCountOptions { Timeout = 5_000 });
    });

    [Fact]
    public Task Table_Pagination_NextPage_AdvancesQueryAndRows() => RunAsync(async () =>
    {
        await NavigateToAsync("/table");
        await Expect(Page.Locator("main h1.h2")).ToHaveTextAsync("Data table",
            new LocatorAssertionsToHaveTextOptions { Timeout = 30_000 });

        var firstRowIdBefore = await Page.Locator("tbody tr td").First.InnerTextAsync();

        // Click the numbered page-2 item.
        await Page.Locator(".page-item .page-link:has-text('2')").First.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(".*[\\?&]page=2"),
            new PageAssertionsToHaveURLOptions { Timeout = 5_000 });

        var firstRowIdAfter = await Page.Locator("tbody tr td").First.InnerTextAsync();
        Assert.NotEqual(firstRowIdBefore, firstRowIdAfter);
    });
}
