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
        Assert.Contains(" min-w-0\"><div class=\"relative block overflow-auto\"><table id=\"ui-fleet\"", page.Html, StringComparison.Ordinal);
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

    [Fact]
    public async Task The_waiting_table_moves_its_pager_and_its_rows_together_when_the_read_is_instant()
    {
        var page = Page.Render(new UiKitDataDisplayDemo(), TestServices.Default());
        var first = page.FindAll("#ui-invoices tbody tr")[0].TextContent;

        await page.On("[data-testid=\"ui-table-waits\"] .hidden button:has-text(\"3\")").Click();

        Assert.Contains("INV-0001", first, StringComparison.Ordinal);
        Assert.Contains("INV-0011", page.FindAll("#ui-invoices tbody tr")[0].TextContent, StringComparison.Ordinal);
        Assert.Equal("3", page.Find("[data-testid=\"ui-table-waits\"] .hidden [aria-current=\"page\"]").TextContent.Trim());
    }

    [Fact]
    public async Task The_waiting_tables_heading_turns_its_rows_round_from_the_first_page()
    {
        var page = Page.Render(new UiKitDataDisplayDemo(), TestServices.Default());
        await page.On("[data-testid=\"ui-table-waits\"] .hidden button:has-text(\"2\")").Click();

        await page.On("#ui-invoices th:has-text(\"Invoice\")").Click();

        Assert.Contains("INV-0040", page.FindAll("#ui-invoices tbody tr")[0].TextContent, StringComparison.Ordinal);
        Assert.Equal("1", page.Find("[data-testid=\"ui-table-waits\"] .hidden [aria-current=\"page\"]").TextContent.Trim());
    }

    [Fact]
    public async Task The_waiting_table_carries_the_marks_its_rows_are_dimmed_by_and_a_switch_that_slows_its_read()
    {
        var page = Page.Render(new UiKitDataDisplayDemo(), TestServices.Default());
        var instant = page.Find("[data-testid=\"ui-invoices-slow\"]").TextContent.Trim();

        await page.On("[data-testid=\"ui-invoices-slow\"]").Click();

        // The pager's root waits for a press inside it, and the sortable heading for its own.
        Assert.Contains("data-ui-pagination data-rask-loading=\"within\"", page.Html, StringComparison.Ordinal);
        Assert.NotNull(page.Find("#ui-invoices th[data-rask-loading]"));
        Assert.Equal("The read is instant", instant);
        Assert.Equal("The read takes 1.5 s", page.Find("[data-testid=\"ui-invoices-slow\"]").TextContent.Trim());
    }
}
