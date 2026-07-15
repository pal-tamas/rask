using System.Linq.Expressions;
using Rask.Core;

namespace Rask.Bootstrap;

/// <summary>
///     The sort a user asked for, reported by <see cref="BsDataGrid{T}.OnSortChange" />: the
///     <see cref="BsColumn{T}.SortField" /> of the column they clicked, and the direction.
/// </summary>
public readonly record struct DataGridSort(string? Field, bool Descending);

// Shared, immutable aria-sort dictionaries. Deliberately non-generic: a static on a generic type is
// per-closed-type, and Mono WASM AOT mis-resolves cached generic statics (see BsSelect). Reusing the
// dictionaries keeps the render path allocation-free — the same trick BsPopover uses for its markers.
internal static class BsGridAria
{
    private static readonly IReadOnlyDictionary<string, string?> SortNone =
        new Dictionary<string, string?> { ["sort"] = "none" };

    private static readonly IReadOnlyDictionary<string, string?> SortAscending =
        new Dictionary<string, string?> { ["sort"] = "ascending" };

    private static readonly IReadOnlyDictionary<string, string?> SortDescending =
        new Dictionary<string, string?> { ["sort"] = "descending" };

    internal static IReadOnlyDictionary<string, string?> Sort(bool sorted, bool descending) =>
        !sorted ? SortNone : descending ? SortDescending : SortAscending;

    // aria-busy marks the table as refetching. It goes on the TABLE rather than on a wrapper enclosing the
    // spinner: role="status" (which BsSpinner renders) is an aria-live region, and a live region inside an
    // aria-busy subtree has its announcement deferred until busy clears — by which point the spinner is gone
    // and the load was never announced at all.
    internal static readonly IReadOnlyDictionary<string, string?> Busy =
        new Dictionary<string, string?> { ["busy"] = "true" };

    // aria-disabled, not the disabled attribute: a real `disabled` drops focus to <body>, which would throw
    // away the user's keyboard position every time a sort or page click starts a fetch. This keeps the control
    // focusable and announced while the handler guards make it inert.
    internal static readonly IReadOnlyDictionary<string, string?> Disabled =
        new Dictionary<string, string?> { ["disabled"] = "true" };
}

// One column of a BsDataGrid<T>. Value gives the cell text (ToString'd); Template overrides it with a
// custom cell. Sortable makes the header a sort toggle, ordering by SortKey (falling back to Value when
// it is IComparable). Class is applied to the header, the cells and the footer cell (e.g. Txt.End() for
// numbers). Footer/FooterTemplate render a summary cell in the table footer (computed over all rows).
public sealed class BsColumn<T>
{
    public string Title { get; init; } = "";
    public Func<T, object?>? Value { get; init; }
    public Func<T, Component>? Template { get; init; }
    public bool Sortable { get; init; }
    public Func<T, IComparable?>? SortKey { get; init; }
    public string? Class { get; init; }

    // Names this column for a controlled sort: it is what BsDataGrid.OnSortChange reports back, and what you
    // translate into an OrderBy. Ignored when the grid sorts in memory (that uses SortKey/Value). A Sortable
    // column with no SortField cannot be sorted that way, so its header stays plain rather than offering a
    // control that would do nothing.
    public string? SortField { get; init; }

    // Orders this column inside a BsDataGrid.Query. It has to be an Expression, not a Func: only an expression
    // tree can be translated to ORDER BY, and a Func would drag the whole table into memory to sort it there —
    // exactly what Query exists to avoid. SortKey is the in-memory equivalent and is ignored in Query mode.
    public Expression<Func<T, object?>>? SortBy { get; init; }

    // Footer summary for this column, computed over the full row set (e.g. a column total). Footer gives
    // text; FooterTemplate overrides it with a custom cell. A grid renders a <tfoot> when any column sets
    // either.
    public Func<IReadOnlyList<T>, object?>? Footer { get; init; }
    public Func<IReadOnlyList<T>, Component>? FooterTemplate { get; init; }

    // Whether BsDataGrid.OnRowClick fires from this column's cells. Null (the default) means AUTO: a Value
    // column is clickable, a Template column is not — and that asymmetry is a safety rule, not a style.
    //
    // The grid attaches the row-click handler to the cells, and the client cancels the default action of any
    // click it dispatches (rask.js: e.preventDefault() runs for every resolved target). Under a handler, a
    // checkbox never fires `change`, an <a href> never navigates, and a bare <button> — which defaults to
    // type=submit — swallows the click instead. Every one of those failures is silent.
    //
    // A Value cell is plain encoded text and can never contain any of them, so it is always safe. A Template
    // cell is exactly where an author puts a link or a button, so it opts out by default. Set true to make a
    // non-interactive template (a badge, an icon) clickable anyway, or false to carve a Value column out.
    public bool? RowClickable { get; init; }

    internal bool HasFooter => Footer is not null || FooterTemplate is not null;

    internal bool IsRowClickable => RowClickable ?? Template is null;

    internal IComparable? Sort(T row) =>
        SortKey is not null ? SortKey(row) : Value?.Invoke(row) as IComparable;

    internal Component Cell(T row) =>
        Template is not null ? Template(row) : (Value?.Invoke(row)?.ToString() ?? "");

    internal Component FooterCell(IReadOnlyList<T> rows) =>
        FooterTemplate is not null ? FooterTemplate(rows)
        : Footer is not null ? (Footer(rows)?.ToString() ?? "")
        : "";
}

// A data grid: typed columns, click-to-sort headers, optional paging, optional per-column footer totals, and
// optional master-detail (an expandable detail row per row via ExpandedContent). Sorting, paging and expansion
// mutate component state and re-render, so it all works with no JavaScript. Cells bind straight to T (no
// view-model). This is the BsTable replacement for list screens.
//
// Where the sorting and paging happen is decided by what you pass as Data:
//   * a list — in memory. Simple, and right for small/medium sets.
//   * an IQueryable — in the store, so the set can be arbitrarily large (server hosts only; see Data).
//   * a list + TotalCount — Data is one already-sorted, already-paged slice you fetched and TotalCount is how
//     many rows are really behind it, so the pager is right. This is the mode with the await in your hands.
public sealed class BsDataGrid<T> : BsBlock
{
    private readonly HashSet<object> _expanded = [];
    private readonly int _instanceId = BsInstanceId.Next();
    private int _page;
    private int _sortColumn = -1;
    private bool _sortDescending;

    // Query mode: the last materialised page, keyed by what produced it. Render runs on every re-render, and
    // without this an unrelated one (expanding a detail row) would re-issue two SQL round-trips.
    private ((IQueryable<T> Query, Expression<Func<T, object?>>? SortBy, int Page, bool Desc, int Size) Key,
        IReadOnlyList<T> Rows, int Total, int PageCount)? _queryCache;

    /// <summary>
    ///     The rows. What you pass decides where the work happens:
    ///     <list type="bullet">
    ///         <item>
    ///             an <see cref="IQueryable{T}" /> (an EF <c>DbSet</c>/query) — the grid orders it by the sorted
    ///             column's <see cref="BsColumn{T}.SortBy" /> expression, counts it, and materialises only the
    ///             current page, so the database does the work. See the remarks: it has real constraints.
    ///         </item>
    ///         <item>anything else — the grid sorts and pages it in memory.</item>
    ///     </list>
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A lazy sequence (<c>rows.Where(...)</c>) is materialised once on arrival, because the grid reads
    ///         the rows several times per render — to count, to sort, and to total the footers — and re-running
    ///         your LINQ chain each time would be a silent tax. Pass a list to skip the copy.
    ///     </para>
    ///     <para>
    ///         <b>An IQueryable has strings attached.</b> Give it a stable order (<c>.OrderBy(p => p.Id)</c>):
    ///         paging a SQL query with no <c>ORDER BY</c> is undefined, and a user sort replaces your ordering
    ///         rather than adding to it. It is executed <b>synchronously</b> inside the render — two round-trips
    ///         blocking a request thread per sort/page click — and re-executed during render, so its
    ///         <c>DbContext</c> must outlive the render (not one from <c>await using var db = ...</c>). It runs
    ///         in-process, so <b>server hosts only</b>; a WASM app has no database and throws on first render.
    ///         Results are cached per (query, sort, page), so an unrelated re-render doesn't re-query.
    ///     </para>
    ///     <para>
    ///         Want none of that? Await your own <c>CountAsync</c>/<c>ToListAsync</c> and pass the page plus
    ///         <see cref="TotalCount" /> — the same feature, with the await in your hands.
    ///     </para>
    /// </remarks>
    public IEnumerable<T>? Data { get; set; }
    public IReadOnlyList<BsColumn<T>>? Columns { get; set; }

    // Rows per page; 0 (default) shows everything with no pager.
    public int PageSize { get; set; } = 0;

    public bool Striped { get; set; } = true;
    public bool Hover { get; set; } = true;
    public bool Small { get; set; } = false;
    public bool Responsive { get; set; } = true;

    // Stable per-row key (defaults to the row index) and an optional empty-state placeholder.
    public Func<T, object?>? RowKey { get; set; }
    public Component? Empty { get; set; }

    // When set, each row gets a leading expander toggle and, when expanded, a full-width detail row built by
    // this callback (master-detail). Requires RowKey for stable expansion across sort/paging.
    public Func<T, Component?>? ExpandedContent { get; set; }

    // ---------------------------------------------------------------------------------------------------
    // Everything below is APPENDED, and must stay appended. The factory generator orders parameters by
    // declaration order within a file, so inserting a property above this line shifts every positional
    // argument after it and silently breaks callers. New parameters go at the end.
    // ---------------------------------------------------------------------------------------------------

    /// <summary>
    ///     Server-side paging: the total number of rows behind <see cref="Data" />. Set it and the grid treats
    ///     <see cref="Data" /> as one already-sorted, already-paged slice — it renders those rows as-is and uses
    ///     this count for the pager. Leave it null and the grid sorts and pages <see cref="Data" /> itself.
    /// </summary>
    /// <remarks>
    ///     Pair it with a controlled <see cref="Page" />/<see cref="Sort" />: the grid tells you what the user
    ///     asked for, you run the query. Footers see only the slice you passed, so compute whole-set totals in
    ///     your query.
    /// </remarks>
    public int? TotalCount { get; set; }

    /// <summary>
    ///     The current 0-based page. Set it to take control of paging — the grid then renders the page you give
    ///     it and reports clicks through <see cref="OnPageChange" /> instead of moving itself. Leave it null and
    ///     the grid owns its own page. Use this to keep the page in the URL, so it survives a reload and a share.
    /// </summary>
    public int? Page { get; set; }

    /// <summary>Raised with the page the user asked for. Only meaningful when <see cref="Page" /> is set.</summary>
    public Callback<int>? OnPageChange { get; set; }

    /// <summary>
    ///     The awaited form of <see cref="OnPageChange" />: the grid awaits it before returning from the click,
    ///     so you can run the query for the new page right here.
    /// </summary>
    public CallbackAsync<int>? OnPageChangeAsync { get; set; }

    /// <summary>
    ///     The <see cref="BsColumn{T}.SortField" /> of the sorted column, or null for unsorted. Set it (with
    ///     <see cref="OnSortChange" />) to take control of sorting the same way <see cref="Page" /> does for
    ///     paging.
    /// </summary>
    public string? Sort { get; set; }

    // The `= false` is load-bearing: a non-nullable property with no initializer becomes a *required* factory
    // parameter (RASK001) and is hoisted ahead of the optional ones — which would break every existing caller.
    /// <summary>Direction of the controlled <see cref="Sort" />.</summary>
    public bool SortDescending { get; set; } = false;

    /// <summary>Raised with the sort the user asked for. Only meaningful when <see cref="Sort" /> is in use.</summary>
    public Callback<DataGridSort>? OnSortChange { get; set; }

    /// <summary>
    ///     The awaited form of <see cref="OnSortChange" />. This is how the grid supports async data without any
    ///     async machinery of its own: the click awaits your handler, you run <c>CountAsync</c>/<c>ToListAsync</c>
    ///     in it and update the state the grid renders from, and the re-render shows the result.
    /// </summary>
    public CallbackAsync<DataGridSort>? OnSortChangeAsync { get; set; }

    /// <summary>
    ///     Extra CSS classes for a row, computed from it — the hook for conditional row styling (a red
    ///     overdue invoice, a muted cancelled order). Return null for no extra class. Applies to data rows
    ///     only, not to a master-detail row.
    /// </summary>
    public Func<T, string?>? RowClass { get; set; }

    /// <summary>
    ///     Raised with the row the user clicked — the "click the row to open it" idiom.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The handler is attached to the row's <b>cells</b>, not to the <c>&lt;tr&gt;</c>, and only to
    ///         those whose column is <see cref="BsColumn{T}.RowClickable" /> (by default: the
    ///         <see cref="BsColumn{T}.Value" /> columns, not the <see cref="BsColumn{T}.Template" /> ones).
    ///         That is what keeps a link, a button or a checkbox inside a template cell working — see
    ///         <see cref="BsColumn{T}.RowClickable" /> for why a handler above one would silently break it.
    ///     </para>
    ///     <para>
    ///         <b>A clickable row is a pointer convenience, not an accessible control.</b> A
    ///         <c>&lt;tr&gt;</c> cannot be made keyboard-operable without either a fake
    ///         <c>role="button"</c> — which destroys the row semantics a screen reader needs — or a tabindex
    ///         on every row, which buries the real controls. So the row click must never be the only way to
    ///         reach the action: put a real link or button in a column too (with
    ///         <c>RowClickable = false</c>) and let the row click duplicate it.
    ///     </para>
    ///     <para>
    ///         It costs one handler per clickable cell, so it scales with rows × columns rather than rows.
    ///     </para>
    /// </remarks>
    public Callback<T>? OnRowClick { get; set; }

    /// <summary>The awaited form of <see cref="OnRowClick" />.</summary>
    public CallbackAsync<T>? OnRowClickAsync { get; set; }

    /// <summary>
    ///     Freezes the header row while the body scrolls under it. Needs <see cref="MaxHeight" />: a sticky
    ///     header sticks to its nearest scroll container, and without a bounded height there is nothing to
    ///     stick to.
    /// </summary>
    public bool? StickyHeader { get; set; }

    /// <summary>
    ///     Bounds the table's height (any CSS length: <c>"400px"</c>, <c>"60vh"</c>) so it scrolls in its own
    ///     box rather than running down the page. Pair it with <see cref="StickyHeader" />. The pager stays
    ///     outside the scroll box.
    /// </summary>
    public string? MaxHeight { get; set; }

    /// <summary>
    ///     Whether a fetch is in flight. Set it around your <see cref="OnPageChangeAsync" /> /
    ///     <see cref="OnSortChangeAsync" /> work and the grid dims the table behind a spinner, marks it
    ///     <c>aria-busy</c>, and ignores further sort/page clicks until it clears.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         It is deliberately <b>nullable</b>, and the three states differ: <c>null</c> (the default) means
    ///         the grid isn't using the feature at all and renders exactly as it always did; <c>false</c> means
    ///         it is in use and idle; <c>true</c> means loading. The distinction is what lets a grid that never
    ///         sets it keep byte-identical markup, while a grid that does gets a wrapper that stays put across
    ///         the flip rather than appearing and disappearing under the table.
    ///     </para>
    ///     <para>
    ///         The empty state is suppressed while loading — a fetch in flight is not "no results", and the
    ///         first load would otherwise flash the placeholder before the rows land.
    ///     </para>
    ///     <para>
    ///         It does nothing for an <see cref="IQueryable" /> <see cref="Data" />: that query runs
    ///         synchronously inside the render, so there is no moment at which the grid is both mounted and
    ///         waiting.
    ///     </para>
    /// </remarks>
    public bool? Loading { get; set; }

    private bool Expandable => ExpandedContent is not null;

    // True only while a fetch is actually in flight. Guards read this; the wrapper keys off `Loading is null`.
    private bool Busy => Loading is true;

    // Controlled: the caller owns the page/sort (typically from the URL), and the grid only reports intent.
    // Uncontrolled: the grid owns them. The two are independent — you can control the sort and not the page.
    private bool PageControlled => Page is not null;

    // Sort can't take the same "is the value set?" signal Page does: Sort = null legitimately means "unsorted",
    // so it cannot be told apart from "not using controlled sort". Any of the three opts in — which also means
    // a Sort passed without a callback still renders sorted rather than being silently discarded.
    private bool SortControlled =>
        Sort is not null || OnSortChange is not null || OnSortChangeAsync is not null;

    private int CurrentPage => Page ?? _page;

    // The sorted column's index. Controlled mode identifies it by SortField (what the URL carries); otherwise
    // it is the index the grid recorded when the header was clicked.
    private int CurrentSortColumn
    {
        get
        {
            if (!SortControlled)
            {
                return _sortColumn;
            }

            if (Sort is null)
            {
                return -1;
            }

            var columns = Columns ?? [];
            for (var i = 0; i < columns.Count; i++)
            {
                if (columns[i].SortField == Sort)
                {
                    return i;
                }
            }

            return -1;
        }
    }

    private bool CurrentSortDescending => SortControlled ? SortDescending : _sortDescending;

    private async Task ToggleSortAsync(int column)
    {
        // The overlay covers the table, but only for a mouse: a keyboard user can still Tab to a header and
        // press Enter. aria-disabled says so; this is what makes it true, and stops a second fetch racing the
        // one already in flight.
        if (Busy)
        {
            return;
        }

        // Clicking the sorted column flips it; a different column starts ascending.
        var descending = CurrentSortColumn == column && !CurrentSortDescending;

        if (SortControlled)
        {
            // The caller owns the sort: report it and let them re-render us with the new props. Resetting the
            // page is their job too — they own it — so OnSortChange carries only the sort.
            var columns = Columns ?? [];
            var field = column >= 0 && column < columns.Count ? columns[column].SortField : null;
            await Raise(OnSortChange, OnSortChangeAsync, new DataGridSort(field, descending));
            return;
        }

        _sortColumn = column;
        _sortDescending = descending;

        // Staying on page 7 of a freshly re-ordered set would show rows the user never asked for.
        if (PageControlled)
        {
            await Raise(OnPageChange, OnPageChangeAsync, 0);
        }
        else
        {
            _page = 0;
        }
    }

    // Sync wins when both are set and the async half is skipped — the contract RASK027 states and every other
    // component follows. Wiring both is a diagnostic, not a supported way to get two calls.
    private static Task Raise<TArg>(Callback<TArg>? sync, CallbackAsync<TArg>? async, TArg arg)
    {
        if (sync is not null)
        {
            sync.Invoke(arg);
            return Task.CompletedTask;
        }

        return async is not null ? async.Invoke(arg) : Task.CompletedTask;
    }

    private void ToggleExpand(object key)
    {
        if (!_expanded.Add(key))
        {
            _expanded.Remove(key);
        }
    }

    // BsPageItem's Disabled only adds a CSS class — the button stays clickable — so the pager's edges have to
    // be guarded here. Without the clamp, prev on page 0 underflows and the summary renders "-1-0 / 3".
    private async Task GoToPageAsync(int page, int pageCount)
    {
        // Same as ToggleSortAsync: BsPageItem's Disabled is CSS-only and the overlay only stops the mouse, so
        // the guard is what actually prevents a second page fetch while one is in flight.
        if (Busy)
        {
            return;
        }

        var target = Math.Clamp(page, 0, Math.Max(0, pageCount - 1));
        if (target == CurrentPage)
        {
            return;
        }

        if (PageControlled)
        {
            await Raise(OnPageChange, OnPageChangeAsync, target);
            return;
        }

        _page = target;
    }

    protected override Component? Render()
    {
        var columns = Columns ?? [];

        // Query: the store orders, counts and pages. TotalCount: Data is already one sorted, paged slice.
        // Otherwise: sort and slice here, and footers see every row because the grid holds them all.
        var (pageRows, footerRows, total, pageCount) =
            Data is IQueryable<T> q ? Queried(q, columns)
            : TotalCount is { } t ? Sliced(t)
            : Local(columns);

        // A fetch in flight is not "no results": without this guard the first load flashes the placeholder
        // before the rows land, and every refetch of an empty filter blinks it back.
        if (total == 0 && Empty is not null && !Busy)
        {
            return Wrap(Empty, null);
        }

        var hasFooter = columns.Any(c => c.HasFooter);

        var table = BsTable(Id: Id, Striped: Striped, Hover: Hover, Small: Small, Responsive: Responsive,
            StickyHeader: StickyHeader, MaxHeight: MaxHeight, Class: Class,
            Aria: Busy ? BsGridAria.Busy : null)[
            Thead()[Tr()[HeaderCells(columns)]],
            Tbody()[BodyRows(columns, pageRows)],
            hasFooter ? Tfoot()[Tr()[FooterCells(columns, footerRows)]] : null
        ];

        return Wrap(table, PageSize > 0 && pageCount > 1 ? Pager(pageCount, total) : null);
    }

    // Loading is null -> the grid never opted in, so return exactly what it always returned: a bare
    // [content, pager] fragment. Byte-identical markup for every grid that doesn't use the feature.
    //
    // Loading is set -> a position-relative wrapper for the overlay to anchor to. It is rendered for BOTH
    // true and false and never comes or goes, which is load-bearing: FrameDiffer matches sibling Elements by
    // TAG NAME alone, so a wrapper that appeared only while loading would leave the differ pairing this
    // <div> against whatever <div> already sat at the slot and morphing one into the other. Keeping it
    // present also preserves the table's DOM identity — and with it focus and scroll position — across a
    // refetch, which is the whole point of showing a spinner instead of replacing the grid.
    //
    // The overlay is appended LAST, after the pager, for the same reason: at the tail it is a pure insert the
    // differ ships as a cheap trusted op, rather than a new element wedged between two existing <div>s.
    // Being position:absolute, its DOM order has no bearing on where it paints.
    private Component Wrap(Component? content, Component? pager) =>
        Loading is null
            ? [content, pager]
            : Div(Class: Position.Relative)[content, pager, Busy ? Overlay() : null];

    // The spinner sits OUTSIDE the aria-busy table (it is a sibling, not a child) so its role="status" live
    // region can actually announce. See BsGridAria.Busy.
    private static Component Overlay() =>
        Div(Class: "bs-grid-overlay")[BsSpinner(Color: BsColor.Primary)];

    // Runs the query: ORDER BY the sorted column's SortBy, COUNT the whole set, and materialise one page.
    // Two round-trips, cached per (query, sort, page) so only a real change pays for them.
    private (IReadOnlyList<T> PageRows, IReadOnlyList<T> FooterRows, int Total, int PageCount) Queried(
        IQueryable<T> query, IReadOnlyList<BsColumn<T>> columns)
    {
        EnsureQueryHost(HostEngine);

        var sortColumn = CurrentSortColumn;
        var sortBy = sortColumn >= 0 && sortColumn < columns.Count ? columns[sortColumn].SortBy : null;
        var page = Math.Max(0, CurrentPage);
        var key = (query, sortBy, page, CurrentSortDescending, PageSize);

        if (_queryCache is { } cached && cached.Key == key)
        {
            return (cached.Rows, cached.Rows, cached.Total, cached.PageCount);
        }

        // OrderBy<T, object?> is called with the expression as-is: no MakeGenericMethod, so nothing here is a
        // trimming or AOT hazard. The boxing convert in the expression is a no-op that providers see through.
        // Unsorted, the caller's own ordering stands — which is why an IQueryable Data wants to arrive
        // ordered: Skip/Take over an unordered query is undefined in SQL. A sort replaces it, not adds to it.
        var ordered = sortBy is null
            ? query
            : CurrentSortDescending
                ? query.OrderByDescending(sortBy)
                : query.OrderBy(sortBy);

        var total = query.Count();
        var pageCount = PageSize > 0 ? Math.Max(1, (total + PageSize - 1) / PageSize) : 1;
        page = Math.Min(page, pageCount - 1);

        var rows = (PageSize > 0 ? ordered.Skip(page * PageSize).Take(PageSize) : ordered).ToList();

        _queryCache = (key, rows, total, pageCount);
        return (rows, rows, total, pageCount);
    }

    // The grid reads the rows several times per render — to count, to sort, and to total the footers — so a
    // lazy sequence gets materialised once here rather than re-running the caller's LINQ chain on each pass.
    // A list costs nothing: the cast succeeds and no copy happens.
    private IReadOnlyList<T> Rows() =>
        Data switch
        {
            null => [],
            IReadOnlyList<T> list => list,
            var lazy => lazy.ToList(),
        };

    // The query runs in-process, so it needs the store in-process. A browser has no database, so say that
    // plainly here rather than let it surface as a null DbContext or a trimmed-away LINQ provider far from the
    // cause. Deliberately gated on Wasm rather than on "not Server": a native host CAN have a local SQLite
    // DbContext, and there an IQueryable is perfectly reasonable.
    internal static void EnsureQueryHost(RenderEngine engine)
    {
        if (engine == RenderEngine.Wasm)
        {
            throw new InvalidOperationException(
                "BsDataGrid was given an IQueryable as Data. It runs the query in-process, and a WASM app has "
                + "no database. Fetch one page over HTTP and pass it as Data with TotalCount instead.");
        }
    }

    // (rows to render, rows the footers total over, total row count, page count)
    //
    // Data is one already-sorted, already-paged slice and TotalCount says how many rows are really behind it.
    // Footers see only this slice — the grid never holds the rest, and inventing a whole-set total from a page
    // would be a lie.
    private (IReadOnlyList<T> PageRows, IReadOnlyList<T> FooterRows, int Total, int PageCount) Sliced(int total)
    {
        var rows = Rows();
        var pageCount = PageSize > 0 ? Math.Max(1, (total + PageSize - 1) / PageSize) : 1;
        return (rows, rows, total, pageCount);
    }

    private (IReadOnlyList<T> PageRows, IReadOnlyList<T> FooterRows, int Total, int PageCount) Local(
        IReadOnlyList<BsColumn<T>> columns)
    {
        var data = Rows();

        IEnumerable<T> view = data;
        var sortColumn = CurrentSortColumn;
        if (sortColumn >= 0 && sortColumn < columns.Count && columns[sortColumn].Sortable)
        {
            var column = columns[sortColumn];
            view = CurrentSortDescending
                ? data.OrderByDescending(column.Sort)
                : data.OrderBy(column.Sort);
        }

        var rows = view as IReadOnlyList<T> ?? view.ToList();
        var pageCount = PageSize > 0 ? Math.Max(1, (rows.Count + PageSize - 1) / PageSize) : 1;
        // Only clamp state we own; a controlled page belongs to the caller.
        if (!PageControlled && _page >= pageCount)
        {
            _page = pageCount - 1;
        }

        var page = Math.Clamp(CurrentPage, 0, Math.Max(0, pageCount - 1));
        var pageRows = PageSize > 0
            ? rows.Skip(page * PageSize).Take(PageSize).ToList()
            : rows;

        // Footers summarise the whole set, not the visible page, so they read `data` rather than pageRows.
        return (pageRows, data, rows.Count, pageCount);
    }

    private IEnumerable<Component> HeaderCells(IReadOnlyList<BsColumn<T>> columns)
    {
        if (Expandable)
        {
            yield return Th(Scope: "col")[""];
        }

        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];

            // A sort the grid cannot actually perform must not advertise a control: a controlled sort is
            // reported by SortField, and a Query is ordered by SortBy. Missing either, the header stays plain.
            if (!column.Sortable
                || (SortControlled && column.SortField is null)
                || (Data is IQueryable<T> && column.SortBy is null))
            {
                yield return Th(Class: column.Class, Scope: "col")[column.Title];
                continue;
            }

            var index = i;
            var sorted = CurrentSortColumn == index;
            var caret = sorted
                ? BsIcon(Name: CurrentSortDescending ? BsIconName.CaretDownFill : BsIconName.CaretUpFill,
                    Class: Margin.Start(1))
                : null;

            // aria-sort advertises the direction to screen readers. The control is a real <button>, so
            // keyboard focus and Enter/Space work with no JS — but Type must be explicit, because <button>
            // defaults to type=submit and a grid inside a <form> would otherwise submit it on every sort.
            yield return Th(Class: column.Class, Scope: "col", Aria: BsGridAria.Sort(sorted, CurrentSortDescending))[
                Button(
                    Type: "button",
                    Class: Bs.Join("btn btn-sm btn-link text-decoration-none", Padding.All(0), Font.Semibold),
                    Aria: Busy ? BsGridAria.Disabled : null,
                    OnClickAsync: () => ToggleSortAsync(index))[column.Title, caret]
            ];
        }
    }

    private IEnumerable<Component> BodyRows(IReadOnlyList<BsColumn<T>> columns, IReadOnlyList<T> pageRows)
    {
        for (var r = 0; r < pageRows.Count; r++)
        {
            var row = pageRows[r];
            var key = RowKey?.Invoke(row) ?? r;
            yield return Tr(Key: key, Class: RowClass?.Invoke(row))[Cells(columns, row, key, r)];

            if (!Expandable || !_expanded.Contains(key))
            {
                continue;
            }

            var detail = ExpandedContent!(row);
            if (detail is not null)
            {
                yield return Tr(Key: $"{key}:detail", Id: DetailId(r))[
                    Td(Colspan: columns.Count + 1)[detail]
                ];
            }
        }
    }

    private IEnumerable<Component> Cells(IReadOnlyList<BsColumn<T>> columns, T row, object key, int index)
    {
        if (Expandable)
        {
            yield return Td()[ExpanderButton(key, index)];
        }

        // Built once and shared by every clickable cell in this row. The callback is per-row, so minting a
        // delegate per cell would multiply the closure allocations by the column count for no benefit —
        // the handler *id* is still per element, which is why OnRowClick scales rows × columns.
        var click = RowClickHandler(row);

        for (var c = 0; c < columns.Count; c++)
        {
            var column = columns[c];
            var clickable = click is not null && column.IsRowClickable;

            yield return Td(
                Class: BsClass.Join(column.Class, clickable ? "bs-grid-click" : null),
                OnClickAsync: clickable ? click : null)[column.Cell(row)];
        }
    }

    // Null when the grid has no row-click wired, which is what keeps every cell handler-free (and the markup
    // byte-identical) for the grids that don't use the feature.
    private CallbackAsync? RowClickHandler(T row) =>
        OnRowClick is null && OnRowClickAsync is null
            ? null
            : () => Raise(OnRowClick, OnRowClickAsync, row);

    // aria-expanded plus a name make the icon-only toggle usable with a screen reader; aria-controls points at
    // the detail row, but only while it is open — ARIA must not reference an id that is not in the document.
    private Component ExpanderButton(object key, int index)
    {
        var expanded = _expanded.Contains(key);
        // Insertion order is emission order, so build it deliberately: expanded, controls, label.
        var aria = new Dictionary<string, string?> { ["expanded"] = expanded ? "true" : "false" };
        if (expanded)
        {
            aria["controls"] = DetailId(index);
        }

        aria["label"] = "Toggle details";

        return BsButton(Color: BsColor.Secondary, Outline: true, Size: BsSize.Sm, Aria: aria,
            OnClick: () => ToggleExpand(key))[
            BsIcon(Name: expanded ? BsIconName.ChevronDown : BsIconName.ChevronRight)];
    }

    private IEnumerable<Component> FooterCells(IReadOnlyList<BsColumn<T>> columns, IReadOnlyList<T> data)
    {
        if (Expandable)
        {
            yield return Td()[""];
        }

        foreach (var column in columns)
        {
            yield return Td(Class: column.Class)[column.FooterCell(data)];
        }
    }

    // A range summary next to a compact pager (prev / windowed page numbers / next), all via Bs components
    // and utilities.
    private Component Pager(int pageCount, int total)
    {
        var from = CurrentPage * PageSize + 1;
        var to = Math.Min((CurrentPage + 1) * PageSize, total);

        return Div(Class: Bs.Join(Display.Flex(), Flex.Justify(BsJustify.Between), Flex.Align(BsAlign.Center)))[
            Span(Class: Bs.Join(Txt.Color(BsColor.Secondary), Font.Small))[$"{from}-{to} / {total}"],
            BsPagination(Size: BsSize.Sm, Class: Margin.Bottom(0))[PageItems(pageCount)]
        ];
    }

    private IEnumerable<Component> PageItems(int pageCount)
    {
        // While loading every item is disabled, not just the edges: the pager is what the user just clicked,
        // so it is where a "wait" has to be visible. BsPageItem renders aria-disabled from this.
        yield return BsPageItem(Key: "prev", Disabled: Busy || CurrentPage == 0,
            OnClickAsync: () => GoToPageAsync(CurrentPage - 1, pageCount))[BsIcon(Name: BsIconName.ChevronLeft)];

        // A small sliding window around the current page keeps the pager compact for many pages.
        var start = Math.Max(0, CurrentPage - 2);
        var end = Math.Min(pageCount - 1, start + 4);
        start = Math.Max(0, end - 4);

        for (var p = start; p <= end; p++)
        {
            var target = p;
            yield return BsPageItem(Key: p, Active: p == CurrentPage, Disabled: Busy,
                OnClickAsync: () => GoToPageAsync(target, pageCount))[(p + 1).ToString()];
        }

        yield return BsPageItem(Key: "next", Disabled: Busy || CurrentPage == pageCount - 1,
            OnClickAsync: () => GoToPageAsync(CurrentPage + 1, pageCount))[BsIcon(Name: BsIconName.ChevronRight)];
    }

    // Keyed by the row's position, not by RowKey: a RowKey is arbitrary user data ("Espresso Machine", a Guid,
    // a composite) and interpolating it produces ids containing spaces. aria-controls is a SPACE-SEPARATED id
    // list, so one space silently turns the reference into two tokens that match nothing — breaking exactly the
    // association it exists to make. The index is always id-safe, and it only has to be unique and resolvable
    // within the render that emits it, which it is. The instance id keeps two grids on one page apart.
    private string DetailId(int index) => $"{Id ?? $"bsgrid{_instanceId}"}-detail-{index}";
}
