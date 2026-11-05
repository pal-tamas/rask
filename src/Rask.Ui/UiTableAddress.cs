using System.Globalization;
using Microsoft.Extensions.Primitives;
using Rask.Core.Live;
using Rask.Core.Routing;

namespace Rask;

/// <summary>
///     What a list's table keeps in the page's address (<c>Ui.Table.SortIn(…)</c>, <c>PageIn(…)</c>): the sorted
///     column and which way it runs, the page, and from those, the pager, the step back past a list's new end, and which
///     of its two empty rows (<c>Empty(…)</c>) the list calls for.
/// </summary>
/// <remarks>
///     The address is read as the table renders and again as a heading is pressed, so two quick presses each turn the
///     sort the address holds by then. A move writes only the table's own keys; every other key the address holds — a
///     filter, a tab — stays.
/// </remarks>
internal sealed class UiTableAddress
{
    /// <summary>The key of the sorted column's name, or <c>null</c> when the list sorts by one column alone.</summary>
    internal string? SortKey { get; set; }

    /// <summary>The key of the direction: <see cref="UiTable.Descending" />, or left out for ascending.</summary>
    internal string? DirectionKey { get; set; }

    /// <summary>The key of the page, counted from one and left out for the first.</summary>
    internal string? PageKey { get; set; }

    /// <summary>How many rows the list has in all; <c>null</c> until it is known.</summary>
    internal int? Total { get; set; }

    /// <summary>How many rows a page holds.</summary>
    internal int PerPage { get; set; } = UiPaginator.DefaultPerPage;

    /// <summary>What the empty row says of a list with nothing in it yet.</summary>
    internal Component? NothingYet { get; set; }

    /// <summary>What the empty row says when what narrows the list leaves nothing.</summary>
    internal Component? NoMatch { get; set; }

    /// <summary>The address of the work in progress, or <c>null</c> with no route (a component rendered on its own).</summary>
    internal static RouteState? Route() => LiveRenderContext.Current?.Services?.GetService(typeof(RouteState)) as RouteState;

    /// <summary>The page the address names: one when it names none, or none that is a page.</summary>
    internal int Page(RouteState? route) =>
        int.TryParse(Read(route, PageKey), NumberStyles.None, CultureInfo.InvariantCulture, out var page) && page > 1 ? page : 1;

    /// <summary>
    ///     The page to step back to: the address names a page past the list's last — its last row was just deleted, or
    ///     the address was typed — so the list is shown from the last page it has.
    /// </summary>
    internal int? StepBack(RouteState? route) =>
        PageKey is not null && Total is { } total && Page(route) > LastPage(total) ? LastPage(total) : null;

    /// <summary>The pager under the rows: a link per page, and none while every row fits on one page.</summary>
    internal Component? Pager(RouteState? route) =>
        PageKey is null || Total is not { } total || total <= Size
            ? null
            : Ui.Pagination.Paginator(new UiPaginator { Page = Page(route), PerPage = Size, Total = total }).Href(page => Link(route, page));

    /// <summary>The keys a move to the list's first page writes: its page taken out.</summary>
    internal KeyValuePair<string, string?>[] FirstPage() => PageKey is { } key ? [new(key, null)] : [];

    /// <summary>
    ///     What the empty row says: <see cref="NoMatch" /> while the list is narrowed — the address holds a key of its own,
    ///     not the table's, or the <see cref="UiFilters" /> that drives it has a filter in force — otherwise
    ///     <see cref="NothingYet" />.
    /// </summary>
    internal Component? Says(RouteState? route, UiFilters? filters) =>
        NoMatch is not null && (filters is { Active: > 0 } || Narrowed(route)) ? NoMatch : NothingYet;

    /// <summary>
    ///     Marks the sortable headings from the address — the column it names, or the first the page marked sorted, or the
    ///     first sortable — and makes each a button that moves the address, unless the page answers its press itself.
    /// </summary>
    /// <param name="parts">The table's parts, its headings among them.</param>
    /// <param name="route">The address.</param>
    /// <exception cref="InvalidOperationException">The address names the column, and a sortable heading has no key to name it by.</exception>
    internal void Sort(IEnumerable<Component?>? parts, RouteState? route)
    {
        if (DirectionKey is null)
        {
            return;
        }

        var columns = Sortable(parts);
        if (columns.Count == 0)
        {
            return;
        }

        var first = columns.Find(column => column.Sorted is true) ?? columns[0];
        var firstName = SortKey is null ? null : Name(first);
        var sorted = Read(route, SortKey) is { } by ? columns.Find(column => string.Equals(Name(column), by, StringComparison.Ordinal)) ?? first : first;
        var descending = Descending(route);
        foreach (var column in columns)
        {
            column.Sorted = ReferenceEquals(column, sorted);
            column.Direction = column.Sorted is true && descending ? Ui.TableColumnDirection.Desc : Ui.TableColumnDirection.Asc;
            if (!column.OnSort.HasValue)
            {
                var name = SortKey is null ? null : Name(column);
                column.OnSort = new Callback(() => Turn(name, firstName));
                BuilderRuntime.MarkCallbacks(column);
            }

            BuilderRuntime.MarkChanged(column);
        }
    }

    // A press on a heading: the sorted column turns round, another starts ascending — and the list starts at its first
    // page. The column the list sorts by unless asked is named by leaving it out, as the first page is.
    private void Turn(string? column, string? firstName)
    {
        var route = Navigator.RequireCurrent().Route;
        var sorted = SortKey is null || string.Equals(Read(route, SortKey) ?? firstName, column, StringComparison.Ordinal);
        var move = new List<KeyValuePair<string, string?>>(3)
        {
            new(DirectionKey!, sorted && !Descending(route) ? UiTable.Descending : null),
        };
        if (SortKey is { } sortKey)
        {
            move.Add(new(sortKey, string.Equals(column, firstName, StringComparison.Ordinal) ? null : column));
        }

        move.AddRange(FirstPage());
        Go.With([.. move]);
    }

    private bool Descending(RouteState? route) =>
        string.Equals(Read(route, DirectionKey), UiTable.Descending, StringComparison.OrdinalIgnoreCase);

    // Whether the address holds a key the table does not: a filter, a tab, a search — whatever narrows the list.
    private bool Narrowed(RouteState? route) =>
        route?.Query.Any(pair => !Own(pair.Key) && !StringValues.IsNullOrEmpty(pair.Value)) is true;

    private bool Own(string key) => Is(key, PageKey) || Is(key, SortKey) || Is(key, DirectionKey);

    private static bool Is(string key, string? own) => own is not null && string.Equals(key, own, StringComparison.OrdinalIgnoreCase);

    private int Size => Math.Max(PerPage, 1);

    private int LastPage(int total) => Math.Max(1, (Math.Max(total, 0) + Size - 1) / Size);

    // Where a page lives: this page with the key written, the first named by leaving it out.
    private RouteUrl Link(RouteState? route, int page)
    {
        var value = page > 1 ? page.ToString(CultureInfo.InvariantCulture) : null;
        return route?.With([new KeyValuePair<string, string?>(PageKey!, value)])
               ?? new RouteUrl(value is null ? "?" : "?" + Uri.EscapeDataString(PageKey!) + "=" + value);
    }

    private static List<UiTableColumn> Sortable(IEnumerable<Component?>? parts) =>
    [
        .. (parts ?? []).OfType<UiTableColumns>()
            .SelectMany(headings => headings.Children ?? [])
            .OfType<UiTableColumn>()
            .Where(column => column.Sortable is true),
    ];

    private static string? Read(RouteState? route, string? key) =>
        key is not null && route is not null && route.Query.TryGetValue(key, out var value) && !StringValues.IsNullOrEmpty(value)
            ? value.ToString()
            : null;

    private static string Name(UiTableColumn column) =>
        Convert.ToString(column.Key, CultureInfo.InvariantCulture) is { Length: > 0 } name
            ? name
            : throw new InvalidOperationException(
                "Ui.Table.SortIn(column, direction) keeps the sorted column in the address by its heading's key, and a "
                + "sortable heading has none. Give each one a key: Ui.TableColumn.Key(\"amount\").Sortable()[\"Amount\"].");
}
