using Rask.Core;
using Rask.Core.Routing;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>A list of suppliers sorted by either column, two to a page, kept in its address.</summary>
[Route("/ui-table-address/suppliers")]
public sealed partial class AddressedSuppliersPage(RouteState route) : Component
{
    [QueryParam("sort")] public string? Sort { get; set; }

    [QueryParam("dir")] public string? Dir { get; set; }

    [QueryParam("page")] public int? Number { get; set; }

    [QueryParam("q")] public string? Search { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var found = AddressedSuppliers.Names.Where(name => Search is null || name.Contains(Search, StringComparison.OrdinalIgnoreCase)).ToList();
        var sorted = Sort == "vat" ? found.OrderBy(AddressedSuppliers.Vat) : found.OrderBy(name => name, StringComparer.Ordinal);
        var page = (Dir == UiTable.Descending ? sorted.Reverse() : sorted).Skip(((Number ?? 1) - 1) * 2).Take(2);

        return Main[
            P.Id("at")[string.Join('&', route.Query.OrderBy(q => q.Key, StringComparer.Ordinal).Select(q => q.Key + "=" + q.Value))],
            Ui.Table.SortIn("sort", "dir").PageIn("page", found.Count, perPage: 2).Empty("No suppliers yet.", "No supplier matches.")[
                Ui.TableColumns[
                    Ui.TableColumn.Key("name").Id("name-heading").Sortable()["Name"],
                    Ui.TableColumn.Key("vat").Id("vat-heading").Sortable()["VAT"]
                ],
                Ui.TableRows[
                    page.Select(name => Ui.TableRow.Key(name)[
                        Ui.TableCell[name],
                        Ui.TableCell[Button.OnClick(() => Delete(name))["Delete " + name]]
                    ])
                ]
            ]
        ];
    }

    // What an app's delete does: the row goes, and the list is read again (here, the page renders again).
    private void Delete(string name)
    {
        AddressedSuppliers.Names.Remove(name);
        StateHasChanged();
    }
}

/// <summary>A list of vehicle groups sorted by their name alone, its direction kept in the address.</summary>
[Route("/ui-table-address/groups")]
public sealed partial class AddressedGroupsPage : Component
{
    [QueryParam("dir")] public string? Dir { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Main[
            P.Id("at")[Dir is null ? "" : "dir=" + Dir],
            Ui.Table.SortIn("dir")[
                Ui.TableColumns[Ui.TableColumn.Id("group-heading").Sortable()["Group"]],
                Ui.TableRows[Ui.TableRow[Ui.TableCell["Vans"]]]
            ]
        ];
}

/// <summary>What the suppliers page lists: a test's own rows, put back before each test.</summary>
internal static class AddressedSuppliers
{
    internal static List<string> Names { get; } = [];

    internal static string Vat(string name) => new([.. name.Reverse()]);
}

/// <summary>
///     <c>Ui.Table.SortIn(…).PageIn(…).Empty(…)</c>: a list page's sort and page kept in the address, the step back past
///     its end, and its two empty rows.
/// </summary>
public sealed partial class UiTableAddressTests : global::Rask.Core.RaskMarkup
{
    // The rows each test starts from: five, so three pages of two.
    public UiTableAddressTests()
    {
        AddressedSuppliers.Names.Clear();
        AddressedSuppliers.Names.AddRange(["Ada", "Bob", "Cyd", "Dee", "Eve"]);
    }

    [Fact]
    public void A_sortable_heading_shows_the_column_and_the_direction_the_address_holds()
    {
        var page = Page.Visit("/ui-table-address/suppliers?sort=vat&dir=desc");

        var vat = Heading(page.Html, "vat-heading");
        var name = Heading(page.Html, "name-heading");

        Assert.Contains("d=\"M4.22 6.22", vat, StringComparison.Ordinal);
        Assert.DoesNotContain("opacity-0", vat, StringComparison.Ordinal);
        Assert.Contains("opacity-0", name, StringComparison.Ordinal);
    }

    [Fact]
    public void With_nothing_in_the_address_the_first_sortable_heading_is_sorted_ascending()
    {
        var page = Page.Visit("/ui-table-address/suppliers");

        var name = Heading(page.Html, "name-heading");

        Assert.Contains("d=\"M11.78 9.78", name, StringComparison.Ordinal);
        Assert.DoesNotContain("opacity-0", name, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_press_on_another_heading_sorts_by_it_ascending_from_the_first_page()
    {
        var page = Page.Visit("/ui-table-address/suppliers?dir=desc&page=2&tab=all");

        await page.On("#vat-heading").Click();

        Assert.Equal("sort=vat&tab=all", page.TextOf("#at"));
    }

    [Fact]
    public async Task A_press_on_the_sorted_heading_turns_it_round_and_back()
    {
        var page = Page.Visit("/ui-table-address/suppliers?sort=vat");

        await page.On("#vat-heading").Click();
        var turned = page.TextOf("#at");
        await page.On("#vat-heading").Click();

        Assert.Equal("dir=desc&sort=vat", turned);
        Assert.Equal("sort=vat", page.TextOf("#at"));
    }

    [Fact]
    public async Task The_column_sorted_unless_asked_is_named_by_leaving_the_key_out()
    {
        var page = Page.Visit("/ui-table-address/suppliers?sort=vat&dir=desc");

        await page.On("#name-heading").Click();

        Assert.Equal("", page.TextOf("#at"));
    }

    [Fact]
    public async Task A_list_sorted_by_one_column_keeps_only_its_direction()
    {
        var page = Page.Visit("/ui-table-address/groups");

        await page.On("#group-heading").Click();
        var turned = page.TextOf("#at");
        await page.On("#group-heading").Click();

        Assert.Equal("dir=desc", turned);
        Assert.Equal("", page.TextOf("#at"));
    }

    [Fact]
    public void The_pager_links_every_page_with_the_rest_of_the_address_kept()
    {
        var page = Page.Visit("/ui-table-address/suppliers?sort=vat&page=2");

        var html = page.Html;

        Assert.Contains("Showing 3 to 4 of 5 results", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/ui-table-address/suppliers?sort=vat&amp;page=3\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/ui-table-address/suppliers?sort=vat\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_list_that_fits_on_one_page_still_draws_its_pager_as_Flux_does()
    {
        AddressedSuppliers.Names.RemoveRange(2, 3);

        var page = Page.Visit("/ui-table-address/suppliers");

        Assert.Contains("Showing 1 to 2 of 2 results", page.Html, StringComparison.Ordinal);
        Assert.Contains("aria-current=\"page\">1</div>", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_list_draws_no_pager_under_its_one_row()
    {
        AddressedSuppliers.Names.Clear();

        var page = Page.Visit("/ui-table-address/suppliers");

        Assert.DoesNotContain("Showing", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deleting_the_last_row_of_the_last_page_steps_back_a_page()
    {
        var page = Page.Visit("/ui-table-address/suppliers?page=3");

        await page.Click("Delete Eve");

        Assert.Equal("page=2", page.TextOf("#at"));
        Assert.Contains("Delete Dee", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_address_past_the_end_lands_on_the_first_page_when_one_is_all_there_is()
    {
        AddressedSuppliers.Names.RemoveRange(1, 4);

        var page = Page.Visit("/ui-table-address/suppliers?sort=vat&page=9");

        Assert.Equal("sort=vat", page.TextOf("#at"));
        Assert.Contains("Delete Ada", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_list_says_nothing_is_in_it_yet_whatever_its_sort()
    {
        AddressedSuppliers.Names.Clear();

        var page = Page.Visit("/ui-table-address/suppliers?sort=vat&dir=desc");

        Assert.Matches("<td [^>]*colspan=\"2\"[^>]*><div [^>]*role=\"status\"[^>]*>No suppliers yet.</div></td>", page.Html);
    }

    [Fact]
    public void An_empty_list_narrowed_by_the_address_says_nothing_matches()
    {
        var page = Page.Visit("/ui-table-address/suppliers?q=zz");

        var html = page.Html;

        Assert.Contains("role=\"status\">No supplier matches.</div></td>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("No suppliers yet.", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_table_whose_rows_are_the_pages_own_says_what_it_was_given_when_it_has_none()
    {
        var table = Ui.Table.Empty("No orders yet.")[Ui.TableColumns[Ui.TableColumn["Order"]], Ui.TableRows];

        var html = table.ToHtml();

        Assert.Matches("<td [^>]*colspan=\"1\"[^>]*><div [^>]*role=\"status\"[^>]*>No orders yet.</div></td>", html);
    }

    [Fact]
    public void A_list_sorted_by_any_column_needs_a_key_on_each_sortable_heading()
    {
        var table = Ui.Table.SortIn("sort", "dir")[Ui.TableColumns[Ui.TableColumn.Sortable()["Order"]], Ui.TableRows];

        var thrown = Assert.ThrowsAny<Exception>(() => table.ToHtml());

        Assert.Contains("Ui.TableColumn.Key(\"amount\").Sortable()", thrown.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_page_of_the_default_paginator_holds_fifteen_rows()
    {
        var paginator = new UiPaginator();

        var perPage = paginator.PerPage;

        Assert.Equal(15, UiPaginator.DefaultPerPage);
        Assert.Equal(UiPaginator.DefaultPerPage, perPage);
    }

    // The markup of the heading with this id, from its <th> to the end of it.
    private static string Heading(string html, string id)
    {
        var start = html.IndexOf("id=\"" + id + "\"", StringComparison.Ordinal);
        return html[start..html.IndexOf("</th>", start, StringComparison.Ordinal)];
    }
}
