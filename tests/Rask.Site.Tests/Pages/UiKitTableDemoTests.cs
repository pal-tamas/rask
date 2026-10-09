using Rask.Site.Features.UiKit;
using Rask.Site.Tests.Infrastructure;

namespace Rask.Site.Tests.Pages;

// The table section of the Data display showcase: the PAGE sorts and pages, the table only draws what it is
// told. Driven in-process; that the chevron shows and the sticky column sticks is the browser journey's.
public sealed class UiKitTableDemoTests
{
    private const string Amounts = "#ui-orders td[class*=\"font-medium\"]";

    // The pager draws its steps twice, for a narrow box and a wide one; the wide one is the hidden-until-wide.
    private const string NextFleetPage = "[data-testid=\"ui-table-stable\"] .hidden button[aria-label^=\"Next\"]";

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

    [Fact]
    public async Task The_fleet_table_states_the_same_widths_on_every_page_and_with_no_rows()
    {
        var page = Page.Render(new UiKitDataDisplayDemo(), TestServices.Default());
        var first = FleetColumns(page);

        await page.On(NextFleetPage).Click();
        var second = FleetColumns(page);
        await page.On("[data-testid=\"ui-fleet-toggle\"]").Click();
        var empty = FleetColumns(page);

        // The table has a width and a floor for the phone; every column but the second has its own.
        Assert.True(page.Find("#ui-fleet").HasClass("w-full") && page.Find("#ui-fleet").HasClass("min-w-160"));
        Assert.Equal(["w-40", "", "w-36", "w-24"], first);
        Assert.Equal(first, second);
        Assert.Equal(first, empty);
        Assert.Empty(page.FindAll("#ui-fleet tbody tr"));
    }

    [Fact]
    public async Task A_long_job_is_cut_in_its_cell_and_whole_in_its_title()
    {
        var page = Page.Render(new UiKitDataDisplayDemo(), TestServices.Default());

        await page.On(NextFleetPage).Click();
        var job = page.FindAll("#ui-fleet tbody td")[1];

        Assert.StartsWith("Full service with brake pads", job.TextContent.Trim(), StringComparison.Ordinal);
        Assert.Equal(job.TextContent.Trim(), job.Attribute("title"));
        Assert.True(job.HasClass("truncate"));
    }

    // The width each heading states, or "" for the one that takes the rest.
    private static List<string> FleetColumns(Page page) =>
        [.. page.FindAll("#ui-fleet thead th").Select(th => th.Classes.FirstOrDefault(name => name.StartsWith("w-", StringComparison.Ordinal)) ?? "")];
}
