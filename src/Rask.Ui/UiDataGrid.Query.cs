using System.Linq.Expressions;

namespace Rask;

public sealed partial class UiDataGrid<T, TKey>
{
    // Asks the source for the page the grid currently wants, unless that is the page it already holds.
    // Called from mount, from a props change, and from the sort and page handlers — every place the
    // request can change.
    private async Task FetchAsync()
    {
        if (Source is not { } source)
        {
            return;
        }

        var request = new UiGridRequest(CurrentSort, CurrentSortDescending, CurrentPage, Paging);
        if (_fetchedFor == request)
        {
            return;
        }

        _fetchedFor = request;
        // `Fn.Invoke` is nullable because an UNSET carrier has nothing to hand back; this one was just
        // matched non-null, so the task it returns is the source's own.
        _fetched = await source.Invoke(request)!.ConfigureAwait(false);
    }

    // What the table draws: the rows of this page, every row the footers total over, how many rows there
    // are altogether, and how many pages that is.
    private readonly record struct Resolved(
        IReadOnlyList<T> Rows, IReadOnlyList<T> All, int Total, int Pages);

    private Resolved ResolveRows(IReadOnlyList<UiColumn<T>> columns)
    {
        // The awaited source has already ordered and sliced; the grid renders what it was handed.
        if (Source is not null)
        {
            var rows = _fetched?.Rows ?? [];
            var total = _fetched?.Total ?? 0;
            return new Resolved(rows, rows, total, PageCount(total));
        }

        // A parent that pages for itself hands over one slice and says how many rows are behind it. The
        // grid must not sort or slice that again: it is already the page that was asked for.
        if (TotalCount is { } given)
        {
            var rows = Data as IReadOnlyList<T> ?? [.. Data ?? []];
            return new Resolved(rows, rows, given, PageCount(given));
        }

        if (Data is IQueryable<T> query)
        {
            return ResolveQuery(query, columns);
        }

        var all = SortInMemory([.. Data ?? []], columns);
        return new Resolved(Slice(all), all, all.Count, PageCount(all.Count));
    }

    // In the store: ORDER BY, then Skip/Take. Every call here is a statically resolved generic over
    // Queryable — nothing reflects, so this survives trimming and AOT untouched.
    private Resolved ResolveQuery(IQueryable<T> query, IReadOnlyList<UiColumn<T>> columns)
    {
        var total = query.Count();

        // The grouped columns lead the ordering, exactly as they do in memory: a band is a run of
        // ADJACENT rows sharing a key, so rows that arrive ungrouped would open the same band twice.
        IOrderedQueryable<T>? ordered = null;
        foreach (var group in GroupColumns(columns))
        {
            if (group.OrderBy is { } band)
            {
                ordered = ordered is null ? query.OrderBy(band) : ordered.ThenBy(band);
            }
        }

        if (SortedColumn(columns) is { OrderBy: { } key })
        {
            if (ordered is null)
            {
                ordered = CurrentSortDescending ? query.OrderByDescending(key) : query.OrderBy(key);
            }
            else
            {
                ordered = CurrentSortDescending ? ordered.ThenByDescending(key) : ordered.ThenBy(key);
            }
        }

        var paged = (IQueryable<T>?)ordered ?? query;
        var pages = PageCount(total);
        if (Paging > 0)
        {
            paged = paged.Skip(Math.Clamp(CurrentPage, 0, Math.Max(pages - 1, 0)) * Paging).Take(Paging);
        }

        var rows = paged.ToList();

        // A footer totals over the WHOLE set, which in query mode means fetching it. Only when a column
        // actually has one — a grid with no footer never pays this — and it is the one place the query
        // path stops being cheap, which is why the property says so.
        //
        // Totalling the page instead would be faster and WRONG: a footer that says "total" while adding
        // up twenty of four thousand rows is a number nobody can tell is a lie.
        var all = columns.Any(static c => c.HasFooter) ? query.ToList() : (IReadOnlyList<T>)rows;
        return new Resolved(rows, all, total, pages);
    }

    private UiColumn<T>? SortedColumn(IReadOnlyList<UiColumn<T>> columns)
    {
        return CurrentSort is { } token ? Find(columns, token) : null;
    }

    // Grouping first, sort second. Bands have to arrive contiguous or a band header would open twice for
    // the same key, so the group keys lead the ordering and the sorted column orders within a band.
    private List<T> SortInMemory(List<T> rows, IReadOnlyList<UiColumn<T>> columns)
    {
        var groups = GroupColumns(columns);
        var sorted = SortedColumn(columns);
        if (groups.Count == 0 && sorted is null)
        {
            return rows;
        }

        IOrderedEnumerable<T>? ordered = null;
        foreach (var band in groups.Select(group => new Func<T, IComparable?>(group.BandOrder)))
        {
            ordered = ordered is null ? rows.OrderBy(band) : ordered.ThenBy(band);
        }

        if (sorted is not null)
        {
            if (ordered is null)
            {
                ordered = CurrentSortDescending ? rows.OrderByDescending(sorted.SortOf) : rows.OrderBy(sorted.SortOf);
            }
            else
            {
                ordered = CurrentSortDescending ? ordered.ThenByDescending(sorted.SortOf) : ordered.ThenBy(sorted.SortOf);
            }
        }

        return ordered is null ? rows : [.. ordered];
    }

    private List<T> Slice(List<T> rows)
    {
        if (Paging <= 0)
        {
            return rows;
        }

        var pages = PageCount(rows.Count);
        var start = Math.Clamp(CurrentPage, 0, Math.Max(pages - 1, 0)) * Paging;
        if (start >= rows.Count)
        {
            return [];
        }

        return rows.GetRange(start, Math.Min(Paging, rows.Count - start));
    }

    private int PageCount(int total) =>
        Paging <= 0 ? 1 : Math.Max(1, (total + Paging - 1) / Paging);
}
