using System.Text.RegularExpressions;
using Rask.Testing;

namespace Rask.Ui.Tests.Components;

/// <summary>
///     The grid's state transitions, driven through its real click handlers.
/// </summary>
/// <remarks>
///     The rendering tests next door see only the first frame, and every one of these paths is reachable
///     solely by clicking. <c>RaskTest</c> dispatches the handlers in-process and re-renders, so none of
///     it needs a browser.
/// </remarks>
public partial class UiDataGridInteractionTests : global::Rask.Core.RaskMarkup
{
    private sealed record Row(int Id, string Name, string Team, int Points);

    private static readonly Row[] Squad =
    [
        new(1, "Banana", "Fruit", 3),
        new(2, "Apple", "Fruit", 5),
        new(3, "Carrot", "Veg", 1),
    ];

    private static global::Rask.Core.Component Grid() =>
        UiDataGrid.Data(Squad).RowKey(r => r.Id)[c => [
            c.Field(r => r.Name).Title("Name").Sortable(true),
            c.Field(r => r.Points).Title("Points").Sortable(true),
        ]];

    // The first body cell of each row, in the order they are rendered — which is the only way to assert
    // that a sort actually REORDERED anything rather than merely flipping an attribute.
    private static string[] Names(string html)
    {
        var body = Regex.Match(html, "<tbody>(.*?)</tbody>", RegexOptions.Singleline).Groups[1].Value;
        return Regex.Matches(body, "<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline)
            .Select(r => Regex.Matches(r.Groups[1].Value, "<td[^>]*>(.*?)</td>", RegexOptions.Singleline))
            .Where(cells => cells.Count > 0)
            .Select(cells => cells[0].Groups[1].Value)
            .ToArray();
    }

    [Fact]
    public async Task Clicking_a_sortable_header_cycles_ascending_descending_then_off()
    {
        var page = RaskTest.Render(Grid());
        Assert.Equal(["Banana", "Apple", "Carrot"], Names(page.Html));

        await page.On("thead button:has-text(\"Name\")").ClickAsync();
        Assert.Equal(["Apple", "Banana", "Carrot"], Names(page.Html));
        Assert.Contains("aria-sort=\"ascending\"", page.Html, StringComparison.Ordinal);

        await page.On("thead button:has-text(\"Name\")").ClickAsync();
        Assert.Equal(["Carrot", "Banana", "Apple"], Names(page.Html));
        Assert.Contains("aria-sort=\"descending\"", page.Html, StringComparison.Ordinal);

        // The third click is the one worth having: it restores the order the caller gave, which no
        // two-state toggle can ever get back to.
        await page.On("thead button:has-text(\"Name\")").ClickAsync();
        Assert.Equal(["Banana", "Apple", "Carrot"], Names(page.Html));
        Assert.DoesNotContain("aria-sort=\"ascending\"", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-sort=\"descending\"", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sorting_by_a_second_column_starts_that_column_ascending()
    {
        var page = RaskTest.Render(Grid());

        await page.On("thead button:has-text(\"Name\")").ClickAsync();
        await page.On("thead button:has-text(\"Points\")").ClickAsync();

        Assert.Equal(["Carrot", "Banana", "Apple"], Names(page.Html));
    }

    [Fact]
    public async Task Sorting_returns_to_the_first_page()
    {
        // Page four of one order names different rows in another, so a new sort that kept the page would
        // land the reader somewhere they never asked to be.
        var page = RaskTest.Render(UiDataGrid.Data(Squad).RowKey(r => r.Id).PageSize(2)[c => [
            c.Field(r => r.Name).Title("Name").Sortable(true),
        ]]);

        await page.On(".join button:has-text(\"2\")").ClickAsync();
        Assert.Equal(["Carrot"], Names(page.Html));

        await page.On("thead button").ClickAsync();
        Assert.Equal(["Apple", "Banana"], Names(page.Html));
    }

    [Fact]
    public async Task Clicking_a_page_shows_it()
    {
        var page = RaskTest.Render(UiDataGrid.Data(Squad).RowKey(r => r.Id).PageSize(2)[c => [
            c.Field(r => r.Name).Title("Name"),
        ]]);

        Assert.Equal(["Banana", "Apple"], Names(page.Html));

        await page.On(".join button:has-text(\"2\")").ClickAsync();
        Assert.Equal(["Carrot"], Names(page.Html));
    }

    [Fact]
    public async Task Ticking_a_row_reports_its_key_at_the_type_it_was_declared_with()
    {
        IReadOnlyList<int>? reported = null;

        var page = RaskTest.Render(UiDataGrid.Data(Squad).RowKey(r => r.Id)
            .OnSelectionChange(keys => reported = keys)[c => [
                c.Field(r => r.Name).Title("Name"),
            ]]);

        await ChangeNth(page, "tbody input", 0, "true");

        // An IReadOnlyList<int>, not a list of boxed objects: the key type reached the callback.
        Assert.NotNull(reported);
        Assert.Equal([1], reported);
    }

    [Fact]
    public async Task Select_all_ticks_every_row_on_the_page_and_only_that_page()
    {
        IReadOnlyList<int>? reported = null;

        var page = RaskTest.Render(UiDataGrid.Data(Squad).RowKey(r => r.Id)
            .PageSize(2)
            .OnSelectionChange(keys => reported = keys)[c => [
                c.Field(r => r.Name).Title("Name"),
            ]]);

        await page.On("thead input").ChangeAsync("true");

        Assert.NotNull(reported);
        Assert.Equal([1, 2], reported!.Order());
    }

    [Fact]
    public async Task An_uncontrolled_selection_survives_the_re_render_it_causes()
    {
        // The strategy that owns the ticked set is rebuilt by the RowKey step on every render. Reusing it
        // rather than replacing it is the whole reason an uncontrolled selection is remembered at all.
        var page = RaskTest.Render(UiDataGrid.Data(Squad).RowKey(r => r.Id)
            .OnSelectionChange(_ => { })[c => [
                c.Field(r => r.Name).Title("Name"),
            ]]);

        await ChangeNth(page, "tbody input", 0, "true");

        Assert.Equal(1, Occurrences(page.Html, "checked"));
    }

    [Fact]
    public async Task Expanding_a_row_reveals_its_detail_and_collapsing_hides_it_again()
    {
        var page = RaskTest.Render(UiDataGrid.Data(Squad).RowKey(r => r.Id).Detail(r => P[r.Team])[c => [
            c.Field(r => r.Name).Title("Name"),
        ]]);

        Assert.DoesNotContain("<p>Fruit</p>", page.Html, StringComparison.Ordinal);

        await ClickNth(page, "tbody button", 0);
        Assert.Contains("<p>Fruit</p>", page.Html, StringComparison.Ordinal);

        await ClickNth(page, "tbody button", 0);
        Assert.DoesNotContain("<p>Fruit</p>", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Grouping_from_a_header_button_bands_the_rows()
    {
        var page = RaskTest.Render(UiDataGrid.Data(Squad).RowKey(r => r.Id)[c => [
            c.Field(r => r.Team).Title("Team").Groupable(true),
            c.Field(r => r.Name).Title("Name"),
        ]]);

        await page.On("[aria-label=\"Group by Team\"]").ClickAsync();

        Assert.Contains("Team: Fruit (2)", page.Html, StringComparison.Ordinal);
        Assert.Contains("Team: Veg (1)", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Collapsing_a_band_hides_only_its_own_rows()
    {
        var page = RaskTest.Render(UiDataGrid.Data(Squad).RowKey(r => r.Id).GroupPanel(true)[c => [
            c.Field(r => r.Team).Title("Team").Groupable(true),
            c.Field(r => r.Name).Title("Name"),
        ]]);

        await page.On("[aria-label=\"Group by Team\"]").ClickAsync();
        Assert.Contains("Banana", page.Html, StringComparison.Ordinal);

        await ClickNth(page, "tbody button", 0);

        // The Fruit band folds shut; Veg is untouched.
        Assert.DoesNotContain("Banana", page.Html, StringComparison.Ordinal);
        Assert.Contains("Carrot", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_group_chip_ungroups_when_its_close_button_is_pressed()
    {
        var page = RaskTest.Render(UiDataGrid.Data(Squad).RowKey(r => r.Id).GroupPanel(true)[c => [
            c.Field(r => r.Team).Title("Team").Groupable(true),
            c.Field(r => r.Name).Title("Name"),
        ]]);

        await page.On("[aria-label=\"Group by Team\"]").ClickAsync();
        Assert.Contains("Team: Fruit (2)", page.Html, StringComparison.Ordinal);

        await page.On("[aria-label=\"Stop grouping by Team\"]").ClickAsync();

        Assert.DoesNotContain("Team: Fruit (2)", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_column_chooser_opens_and_hides_a_column()
    {
        var page = RaskTest.Render(UiDataGrid.Data(Squad).RowKey(r => r.Id).ColumnChooser(true)[c => [
            c.Field(r => r.Name).Title("Name"),
            c.Field(r => r.Points).Title("Points"),
        ]]);

        await page.On("button:has-text(\"Columns\")").ClickAsync();
        Assert.Equal(2, Occurrences(page.Html, "checkbox-xs"));

        // The second box in the menu is the Points column.
        await ChangeNth(page, "[aria-label=\"Columns\"] input", 1, "false");

        Assert.Equal(1, Occurrences(page.Html, "<th "));
    }

    [Fact]
    public async Task Moving_a_column_down_reorders_the_table()
    {
        var page = RaskTest.Render(UiDataGrid.Data(Squad).RowKey(r => r.Id).ColumnChooser(true)[c => [
            c.Field(r => r.Name).Title("Name"),
            c.Field(r => r.Points).Title("Points"),
        ]]);

        await page.On("button:has-text(\"Columns\")").ClickAsync();
        await ClickNth(page, "[aria-label=\"Move down\"]", 0);

        var head = Regex.Match(page.Html, "<thead.*?</thead>", RegexOptions.Singleline).Value;
        Assert.True(head.IndexOf("Points", StringComparison.Ordinal)
            < head.IndexOf("Name", StringComparison.Ordinal));
    }

    // ---- the awaited source -----------------------------------------------------------------------

    [Fact]
    public async Task An_awaited_source_is_asked_for_the_page_the_grid_wants()
    {
        var asked = new List<UiGridRequest>();

        // Stated rather than inferred: the rows arrive through Source, whose carrier is a struct a
        // lambda reaches by conversion, so nothing about it can pin the grid's type.
        var page = RaskTest.Render(UiDataGrid.Of<Row, int>()
            .RowKey(r => r.Id)
            .PageSize(2)
            .Source(request =>
            {
                asked.Add(request);
                return Task.FromResult(new UiGridPage<Row>(
                    [.. Squad.Skip(request.Page * 2).Take(2)], Squad.Length));
            })[c => [
                c.Field(r => r.Name).Title("Name"),
            ]]);

        await page.WaitForAsync(html => html.Contains("Banana", StringComparison.Ordinal));
        Assert.Equal(["Banana", "Apple"], Names(page.Html));

        await page.On(".join button:has-text(\"2\")").ClickAsync();

        Assert.Equal(["Carrot"], Names(page.Html));
        Assert.Equal(2, asked.Count);
        Assert.Equal(0, asked[0].Page);
        Assert.Equal(1, asked[1].Page);
        Assert.Equal(2, asked[1].PageSize);
    }

    [Fact]
    public async Task An_awaited_source_is_not_asked_again_for_the_page_it_already_holds()
    {
        var calls = 0;

        var page = RaskTest.Render(UiDataGrid.Of<Row, int>()
            .RowKey(r => r.Id)
            .Source(_ =>
            {
                calls++;
                return Task.FromResult(new UiGridPage<Row>(Squad, Squad.Length));
            })[c => [
                c.Field(r => r.Name).Title("Name"),
            ]]);

        await page.WaitForAsync(html => html.Contains("Banana", StringComparison.Ordinal));
        page.Render();
        page.Render();

        Assert.Equal(1, calls);
    }

    // The nth match, by handler id. RenderedComponent.Find refuses an ambiguous selector on purpose —
    // a test that silently drove the first of three checkboxes would pass for the wrong reason — so a
    // repeated control is addressed by position rather than by a selector that only looks specific.
    private static Task<string> ClickNth(RenderedComponent page, string selector, int index) =>
        page.InvokeAsync(page.FindAll(selector)[index].Attributes["data-rask-on-click"]!);

    private static Task<string> ChangeNth(
        RenderedComponent page, string selector, int index, string value) =>
        page.InvokeAsync(
            page.FindAll(selector)[index].Attributes["data-rask-on-change"]!,
            $"{{\"value\":\"{value}\"}}");

    private static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        var at = 0;
        while ((at = haystack.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }

        return count;
    }
}
