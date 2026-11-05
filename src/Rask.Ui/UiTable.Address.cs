using System.Globalization;
using Rask.Core.Routing;

namespace Rask;

// A list page's table: its sort and its page kept in the page's address, the step back past its end, two empty rows.
public sealed partial class UiTable
{
    /// <summary>The <see cref="UiFilters" /> that drives this table, whose filters in force narrow the list.</summary>
    internal UiFilters? Filters { get; set; }

    /// <summary>
    ///     Keeps the direction of the sorted column in the page's address, under <paramref name="direction" />: a
    ///     sortable heading's press writes it there, and the heading shows what the address says.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Not Flux's: an addition the owner approved (2026-11-04) for a list page. The list sorts by one column — the
    ///         first sortable heading, or the one the page marks <c>Sorted()</c>. A press turns it round, as Flux's does:
    ///         <see cref="Descending" /> in the address, ascending with the key left out. The page reads the key as its
    ///         own query parameter and sorts its rows by it; a sort takes the list back to its first page.
    ///     </para>
    ///     <code>
    ///     [QueryParam("dir")] public string? Dir { get; set; }
    ///
    ///     Ui.Table.Of(groups).SortIn("dir")[
    ///         Ui.TableColumns[Ui.TableColumn.Sortable()["Name"]],
    ///         …
    ///     ]
    ///     </code>
    ///     A heading the page gives its own <c>OnSort</c> keeps it: the page answers that press itself.
    /// </remarks>
    /// <param name="direction">The query key the direction is kept under: <c>"dir"</c>.</param>
    /// <returns>This table.</returns>
    public UiTable SortIn(string direction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(direction);
        var address = Address();
        address.SortKey = null;
        address.DirectionKey = direction;
        return this;
    }

    /// <summary>
    ///     As <see cref="SortIn(string)" />, for a list sorted by any of several columns: the column pressed is kept in
    ///     the address too, under <paramref name="column" />, by its heading's key.
    /// </summary>
    /// <remarks>
    ///     A press on another heading sorts by it, ascending; a press on the sorted one turns it round. The column the
    ///     list sorts by unless asked — the heading the page marks <c>Sorted()</c>, or the first sortable — is named by
    ///     leaving the key out, as the first page is.
    ///     <code>
    ///     [QueryParam("sort")] public string? Sort { get; set; }
    ///     [QueryParam("dir")] public string? Dir { get; set; }
    ///
    ///     Ui.Table.Of(suppliers).SortIn("sort", "dir")[
    ///         Ui.TableColumns[
    ///             Ui.TableColumn.Key("name").Sortable()["Name"],
    ///             Ui.TableColumn.Key("vat").Sortable()["VAT number"]
    ///         ],
    ///         …
    ///     ]
    ///     </code>
    /// </remarks>
    /// <param name="column">The query key the sorted column's key is kept under: <c>"sort"</c>.</param>
    /// <param name="direction">The query key the direction is kept under: <c>"dir"</c>.</param>
    /// <returns>This table.</returns>
    public UiTable SortIn(string column, string direction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(column);
        ArgumentException.ThrowIfNullOrWhiteSpace(direction);
        var address = Address();
        address.SortKey = column;
        address.DirectionKey = direction;
        return this;
    }

    /// <summary>
    ///     Keeps the page of the list in the page's address, under <paramref name="page" />: a pager of links under the
    ///     rows, drawn as Flux draws it even when they fit on one page, and — when the address names a page past the
    ///     list's end, as it does once the last row of the last page is deleted — a step back to the last page there is.
    /// </summary>
    /// <remarks>
    ///     Not Flux's: an addition the owner approved (2026-11-04) for a list page. The first page is named by leaving the
    ///     key out. The page reads the key as its own query parameter and reads that page of its rows; a sort, and a
    ///     filter of a <see cref="UiFilters" /> before the table, take the list back to its first page.
    ///     <code>
    ///     [QueryParam("page")] public int? Page { get; set; }
    ///
    ///     Ui.Table.Of(listed).PageIn("page", listed.Data?.Total)[…]
    ///     </code>
    /// </remarks>
    /// <param name="page">The query key the page is kept under: <c>"page"</c>.</param>
    /// <param name="total">How many rows the list has in all; <c>null</c> while it is not known, with no pager meanwhile.</param>
    /// <param name="perPage">How many rows a page holds: <see cref="UiPaginator.DefaultPerPage" /> unless the list says otherwise.</param>
    /// <returns>This table.</returns>
    public UiTable PageIn(string page, int? total, int perPage = UiPaginator.DefaultPerPage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(page);
        ArgumentOutOfRangeException.ThrowIfLessThan(perPage, 1);
        var address = Address();
        address.PageKey = page;
        address.Total = total;
        address.PerPage = perPage;
        return this;
    }

    /// <summary>
    ///     What the one row of a list with no rows says: <paramref name="nothingYet" /> while nothing is in it,
    ///     <paramref name="noMatch" /> while what narrows it leaves nothing.
    /// </summary>
    /// <remarks>
    ///     Not Flux's: an addition the owner approved (2026-11-04) for a list page. The list is narrowed while the page's
    ///     address holds a key that is not the table's own sort or page — a filter, a tab, a search — or a
    ///     <see cref="UiFilters" /> before the table has a filter in force. The headings stay above the row, and the row
    ///     reaches across every column.
    ///     <code>
    ///     Ui.Table.Of(drivers).Empty("No drivers yet.", "No driver matches the filters.")[…]
    ///     </code>
    /// </remarks>
    /// <param name="nothingYet">What the row says of a list with nothing in it yet.</param>
    /// <param name="noMatch">What it says when the filters leave nothing; <paramref name="nothingYet" /> when not given.</param>
    /// <returns>This table.</returns>
    public UiTable Empty(Component? nothingYet, Component? noMatch = null)
    {
        var address = Address();
        address.NothingYet = nothingYet;
        address.NoMatch = noMatch;
        // A table whose rows are the page's own, not a query's, has them all: its empty row shows when it has none.
        _query ??= new UiTableLoad();
        return this;
    }

    /// <summary>The keys a filter change writes beside its own so the list starts at its first page.</summary>
    internal KeyValuePair<string, string?>[] FirstPage() => _address?.FirstPage() ?? [];

    private UiTableAddress Address() => _address ??= new UiTableAddress();

    // A list whose address names a page past its end — its last row deleted, or the address typed — goes on from its last
    // page, replacing the address, as a page that sends the reader on as it mounts does. Only where there is an address
    // to move: a table rendered on its own has none.
    private void StepBack(RouteState? route)
    {
        if (route is not null && _address?.StepBack(route) is { } page && _address.PageKey is { } key)
        {
            Go.With(key, page > 1 ? page.ToString(CultureInfo.InvariantCulture) : null);
        }
    }
}
