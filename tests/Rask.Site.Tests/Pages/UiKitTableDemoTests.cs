using Rask.Site.Features.UiKit;
using Rask.Site.Tests.Infrastructure;

namespace Rask.Site.Tests.Pages;

// The table section of the Data display showcase: the PAGE sorts and pages, the table only draws what it is
// told. Driven in-process; that the chevron shows and the sticky column sticks is the browser journey's.
public sealed class UiKitTableDemoTests
{
    private const string Amounts = "#ui-orders td[class*=\"font-medium\"]";

    private static string FirstAmount(Page page) => page.FindAll(Amounts)[0].TextContent.Trim();

    [Fact]
    public async Task Sorting_by_a_heading_reorders_the_rows_and_a_second_click_turns_them_round()
    {
        var page = Page.Render(new UiKitDataDisplayDemo(), TestServices.Default());
        Assert.Equal("$32.00", FirstAmount(page));

        await page.On("#ui-orders th:has-text(\"Amount\")").Click();
        var ascending = FirstAmount(page);
        await page.On("#ui-orders th:has-text(\"Amount\")").Click();

        Assert.Equal("$12.00", ascending);
        Assert.Equal("$313.00", FirstAmount(page));
    }

    [Fact]
    public async Task The_pager_under_the_table_moves_through_the_sorted_rows()
    {
        var page = Page.Render(new UiKitDataDisplayDemo(), TestServices.Default());

        await page.On("[data-testid=\"ui-table\"] button:has-text(\"3\")").Click();

        // Newest first, four to a page: the third page starts at the ninth order.
        Assert.Equal("$313.00", FirstAmount(page));
    }
}
