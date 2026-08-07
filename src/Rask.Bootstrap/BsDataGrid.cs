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

    // "Select all" would be a lie wherever a pager is: the grid holds one page and can only name its keys.
    internal static readonly IReadOnlyDictionary<string, string?> SelectPage =
        new Dictionary<string, string?> { ["label"] = "Select all rows on this page" };

    // The fallback name for a row checkbox, used only when no column has text to borrow.
    internal static readonly IReadOnlyDictionary<string, string?> SelectRow =
        new Dictionary<string, string?> { ["label"] = "Select row" };
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

    /// <summary>
    ///     The column's stable identity: <c>Field = p => p.Category</c> names this column <c>"category"</c>.
    ///     That token is what <see cref="BsDataGrid{T}.OnSortChange" /> / <see cref="BsDataGrid{T}.OnGroupedChange" />
    ///     report and what belongs in a URL, and the same tree doubles as the store's <c>ORDER BY</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         An expression rather than a string because the name is <b>read off the member</b>, so it cannot
    ///         drift from the property it describes. <see cref="Value" /> could never supply it: it is a
    ///         <c>Func&lt;T, object?&gt;</c> — a compiled delegate carries no member name.
    ///     </para>
    ///     <para>
    ///         Only the member name is read; the expression is never compiled, so it costs nothing per render.
    ///         Set <see cref="SortField" /> or <see cref="SortBy" /> to override either derived use.
    ///     </para>
    /// </remarks>
    public Expression<Func<T, object?>>? Field { get; init; }

    /// <summary>Makes this column offer a "group by" control. Needs <see cref="Field" /> (or nothing to name it).</summary>
    public bool Groupable { get; init; }

    /// <summary>
    ///     Whether the column chooser may hide this column. Default true — hiding costs nothing, so nearly every
    ///     column should be manageable. Set false to pin a column the grid is unusable without (an identity
    ///     column, a row-action column): it gets no hide toggle and <see cref="BsDataGrid{T}.HiddenColumns" />
    ///     cannot drop it.
    /// </summary>
    public bool Hideable { get; init; } = true;

    /// <summary>
    ///     Whether the user may reorder this column. Default true. Set false to anchor a column at its declared
    ///     position (a leading name column, say) — <see cref="BsDataGrid{T}.ColumnOrder" /> leaves it put and the
    ///     other columns flow around it.
    /// </summary>
    public bool Reorderable { get; init; } = true;

    /// <summary>
    ///     The value rows are banded by when grouped, defaulting to <see cref="Value" />. Set it when the band
    ///     should be coarser than the cell — group orders by month, show the full date.
    /// </summary>
    public Func<T, object?>? GroupKey { get; init; }

    /// <summary>
    ///     Renders the band header's content from the band's key and its rows on this page. Defaults to
    ///     "{Title}: {key} ({n})".
    /// </summary>
    public Func<object?, IReadOnlyList<T>, Component>? GroupHeader { get; init; }

    internal bool HasFooter => Footer is not null || FooterTemplate is not null;

    internal bool IsRowClickable => RowClickable ?? Template is null;

    // The member name off Field, camelCased ("Category" -> "category") so it reads as a URL token. Computed
    // once per column instance: columns are usually rebuilt each render, but the walk is a couple of casts —
    // no Compile, so nothing here is a per-render or AOT cost.
    private string? _name;
    private bool _named;

    internal string? FieldName
    {
        get
        {
            if (_named)
            {
                return _name;
            }

            _named = true;
            var body = Field?.Body;

            // p => p.Category against an object? return type is lowered to Convert(p.Category, object), so the
            // boxing conversion has to come off before the member is visible. Same unwrap ExpressionAccessor
            // does for a bound field.
            if (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } u)
            {
                body = u.Operand;
            }

            if (body is MemberExpression m)
            {
                var n = m.Member.Name;
                _name = char.IsUpper(n[0]) ? char.ToLowerInvariant(n[0]) + n[1..] : n;
            }

            return _name;
        }
    }

    // Field is the single source, but the older explicit properties still win where they are set: SortField
    // is shipped API that callers already pass to OnSortChange, and SortBy may deliberately order by
    // something other than the column's own member (a related entity, a computed key).
    internal string? SortToken => SortField ?? FieldName;

    internal Expression<Func<T, object?>>? OrderBy => SortBy ?? Field;

    internal object? Group(T row) => GroupKey is not null ? GroupKey(row) : Value?.Invoke(row);

    // Ordering key for a band. Banding compares keys by equality, but the rows have to ARRIVE grouped, and
    // that ordering needs an IComparable — the same shape Sort() uses for a sorted column.
    internal IComparable? GroupSort(T row) => Group(row) as IComparable;

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

    // Uncontrolled selection. It is keyed, not indexed, so it survives sorting and paging — and it deliberately
    // accumulates across pages: picking three rows on page 1 and two on page 2 is five selected rows, which is
    // the whole point of a bulk action.
    private readonly HashSet<object> _selected = [];

    // Uncontrolled grouping, outermost first.
    private readonly List<string> _grouped = [];

    // Uncontrolled column visibility (hidden field tokens) and display order (field tokens, first to last).
    // Both mirror _grouped: a token list the grid owns when the caller doesn't control it.
    private readonly List<string> _hidden = [];
    private readonly List<string> _order = [];

    // Whether the "Columns" menu is open. Zero-JS disclosure, keyed on the grid like _expanded/_collapsed.
    private bool _chooserOpen;

    // Collapsed bands, keyed by their composite VALUE path ("FruitApple"), never by index: a band's
    // position changes with every sort and page, and collapse must follow the band rather than the slot.
    private readonly HashSet<string> _collapsed = [];

    // The field currently being dragged in the group panel. The whole state a group drag needs — the dragged
    // identity rides here rather than in the event payload, which is why the client's messages stay {id,type}.
    private string? _dragField;
    private int _page;
    private int _sortColumn = -1;
    private bool _sortDescending;

    // Query mode: the last materialised page, keyed by what produced it. Render runs on every re-render, and
    // without this an unrelated one (expanding a detail row) would re-issue two SQL round-trips.
    // Grouped is part of the key, not an afterthought: it changes the ORDER BY, so a cache hit across a
    // regroup would band the page by the new fields while the rows were still ordered by the old ones —
    // producing repeated bands from a query that was never re-run.
    private ((IQueryable<T> Query, Expression<Func<T, object?>>? SortBy, int Page, bool Desc, int Size,
        string Grouped) Key,
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
    public new IEnumerable<T>? Data { get; set; }
    public IReadOnlyList<BsColumn<T>>? Columns { get; set; }

    // Rows per page; 0 (default) shows everything with no pager.
    public int PageSize { get; set; } = 0;

    public bool Striped { get; set; } = true;
    public bool Hover { get; set; } = true;
    public new bool Small { get; set; } = false;
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

    /// <summary>
    ///     Adds a leading checkbox column so rows can be picked for a bulk action. Implied by
    ///     <see cref="SelectedKeys" /> / <see cref="OnSelectionChange" />, so it is only needed for a grid
    ///     that keeps its own selection.
    /// </summary>
    /// <remarks>
    ///     <b>Set <see cref="RowKey" /> with it.</b> Selection is tracked by key, and without one the grid
    ///     falls back to the row index — so the selection would follow the third *position* across a sort or a
    ///     page change rather than the row you picked. RASK033 flags this at the call site.
    /// </remarks>
    public bool? Selectable { get; set; }

    /// <summary>
    ///     The selected rows' <see cref="RowKey" /> values. Set it to take control of the selection the same
    ///     way <see cref="Page" /> does for paging — the grid then renders the selection you give it and
    ///     reports clicks through <see cref="OnSelectionChange" /> instead of tracking its own. Leave it null
    ///     and the grid owns the selection.
    /// </summary>
    public IReadOnlyList<object>? SelectedKeys { get; set; }

    /// <summary>
    ///     Raised with the full set of selected keys after a click — not a delta.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         It reports <b>keys, not rows</b>. Under <see cref="TotalCount" /> or an <see cref="IQueryable" />
    ///         the grid only ever holds the current page, so it cannot turn a key from a page you have left
    ///         back into a row. Map them yourself — and <b>re-check them server-side</b>: a key can name a row
    ///         that has since been deleted or that this user may not touch.
    ///     </para>
    /// </remarks>
    public Callback<IReadOnlyList<object>>? OnSelectionChange { get; set; }

    /// <summary>The awaited form of <see cref="OnSelectionChange" />.</summary>
    public CallbackAsync<IReadOnlyList<object>>? OnSelectionChangeAsync { get; set; }

    /// <summary>
    ///     The columns to band rows by, outermost first, named by <see cref="BsColumn{T}.Field" /> — so this is
    ///     URL-serialisable (<c>?group=category,supplier</c>). Set it to take control of grouping the same way
    ///     <see cref="Sort" /> does for sorting; leave it null and the grid owns its own.
    /// </summary>
    /// <remarks>
    ///     A band is a run of <b>consecutive</b> rows sharing the key, so the rows must arrive ordered by it.
    ///     The grid guarantees that wherever it owns the ordering — in memory, and in an
    ///     <see cref="IQueryable" /> (it prepends the group columns to the <c>ORDER BY</c>). Under
    ///     <see cref="TotalCount" /> it never sees the whole set and cannot: order by these fields in your own
    ///     query, which is exactly what <see cref="OnGroupedChange" /> hands you.
    /// </remarks>
    public IReadOnlyList<string>? Grouped { get; set; }

    /// <summary>Raised with the group fields the user asked for, outermost first.</summary>
    public Callback<IReadOnlyList<string>>? OnGroupedChange { get; set; }

    /// <summary>The awaited form of <see cref="OnGroupedChange" />.</summary>
    public CallbackAsync<IReadOnlyList<string>>? OnGroupedChangeAsync { get; set; }

    /// <summary>Lets a band header collapse its rows. Zero-JS, like the master-detail expander.</summary>
    public bool? GroupCollapsible { get; set; }

    /// <summary>
    ///     Renders a subtotal row at the end of each band, reusing each column's
    ///     <see cref="BsColumn{T}.Footer" />/<see cref="BsColumn{T}.FooterTemplate" /> over that band's rows.
    /// </summary>
    /// <remarks>
    ///     A subtotal only ever sums the rows <b>on this page</b> — exactly as the grand footer already does
    ///     under <see cref="TotalCount" /> or an <see cref="IQueryable" />, and for the same reason: the page
    ///     is all the grid holds.
    /// </remarks>
    public bool? GroupSubtotals { get; set; }

    /// <summary>
    ///     Renders a panel above the grid holding a chip per group level, and a "group by" control on every
    ///     <see cref="BsColumn{T}.Groupable" /> header — so the user can group, renest and ungroup.
    /// </summary>
    /// <remarks>
    ///     Every action has a real <c>&lt;button&gt;</c>: the chips carry ungroup and move-in/move-out, and the
    ///     headers carry group-by. Dragging a header into the panel, reordering the chips and dragging one out
    ///     do the same things faster for a mouse. That ordering is deliberate — drag is an accelerator, and a
    ///     feature whose primary action is drag-only cannot be reached by keyboard at all.
    /// </remarks>
    public bool? GroupPanel { get; set; }

    /// <summary>
    ///     Whether a grouped column stays in the table. A grouped column's value is the same for every row in
    ///     its band and already names the band header, so by default (null/false) the column is folded away
    ///     while it is grouped — its header, cells, subtotal and footer are dropped and the colspans shrink to
    ///     match. Set true to keep it, so the value shows both in the band header and repeated down every row.
    /// </summary>
    public bool? ShowGroupedColumns { get; set; }

    /// <summary>
    ///     The <see cref="BsColumn{T}.Field" /> names of columns to hide from the table, so this is
    ///     URL-serialisable (<c>?hide=discount,notes</c>) exactly like <see cref="Grouped" />. Set it to take
    ///     control of visibility the same way <see cref="Grouped" /> does for grouping; leave it null and the grid
    ///     owns its own. Unknown, unnamed or <see cref="BsColumn{T}.Hideable" />-false tokens are ignored (a URL
    ///     is user input), and a set that would hide every column is refused wholesale — the grid never renders a
    ///     bodyless table.
    /// </summary>
    public IReadOnlyList<string>? HiddenColumns { get; set; }

    /// <summary>Raised with the full set of hidden tokens after a toggle — not a delta.</summary>
    public Callback<IReadOnlyList<string>>? OnHiddenColumnsChange { get; set; }

    /// <summary>The awaited form of <see cref="OnHiddenColumnsChange" />.</summary>
    public CallbackAsync<IReadOnlyList<string>>? OnHiddenColumnsChangeAsync { get; set; }

    /// <summary>
    ///     The <see cref="BsColumn{T}.Field" /> names in display order, so this is URL-serialisable
    ///     (<c>?cols=name,price,region</c>). It is partial and stale-tolerant: unknown tokens are dropped, and any
    ///     named column the list omits is appended in declaration order — so adding a column to
    ///     <see cref="Columns" /> without touching a persisted order lands it predictably at the end rather than
    ///     vanishing. Columns with no <see cref="BsColumn{T}.Field" /> (or <see cref="BsColumn{T}.Reorderable" />
    ///     false) are fixtures held at their declared slot.
    /// </summary>
    public IReadOnlyList<string>? ColumnOrder { get; set; }

    /// <summary>Raised with the full column order, outermost first, after a move.</summary>
    public Callback<IReadOnlyList<string>>? OnColumnOrderChange { get; set; }

    /// <summary>The awaited form of <see cref="OnColumnOrderChange" />.</summary>
    public CallbackAsync<IReadOnlyList<string>>? OnColumnOrderChangeAsync { get; set; }

    /// <summary>
    ///     Renders a "Columns" menu above the grid: a checkbox to show or hide each column, and move earlier/later
    ///     buttons to reorder it. Implied by <see cref="HiddenColumns" />/<see cref="ColumnOrder" /> (and their
    ///     callbacks), so it is only needed for a grid that keeps its own layout. With it on, a column header also
    ///     becomes a drag source for reordering — drop a header on another header to move it.
    /// </summary>
    /// <remarks>
    ///     The menu never lists a column with no <see cref="BsColumn{T}.Field" /> (nothing to name it in a URL).
    ///     Every action is a real <c>&lt;button&gt;</c> or checkbox, so the whole feature works from the keyboard
    ///     alone; header drag is a mouse accelerator layered on top. The menu shares the toolbar strip with the
    ///     <see cref="GroupPanel" /> when both are on.
    /// </remarks>
    public bool? ColumnChooser { get; set; }

    private bool Expandable => ExpandedContent is not null;

    // Same three-way opt-in Sort uses, and for the same reason: Grouped = null legitimately means "ungrouped"
    // and cannot be told apart from "not using controlled grouping", so any of the three opts in.
    private bool GroupControlled =>
        Grouped is not null || OnGroupedChange is not null || OnGroupedChangeAsync is not null;

    private IReadOnlyList<string> CurrentGrouped => GroupControlled ? Grouped ?? [] : _grouped;

    // Column visibility and order follow the same three-way opt-in as Grouped: an empty list is a legitimate
    // controlled value ("nothing hidden" / "no explicit order"), so any of the three signals opts in.
    private bool HideControlled =>
        HiddenColumns is not null || OnHiddenColumnsChange is not null || OnHiddenColumnsChangeAsync is not null;

    private IReadOnlyList<string> CurrentHidden => HideControlled ? HiddenColumns ?? [] : _hidden;

    private bool OrderControlled =>
        ColumnOrder is not null || OnColumnOrderChange is not null || OnColumnOrderChangeAsync is not null;

    private IReadOnlyList<string> CurrentOrder => OrderControlled ? ColumnOrder ?? [] : _order;

    // Header drag reorders when the chooser is on or a controlled order is in use — the same gate the drag
    // source on the header reads.
    private bool ReorderEnabled => ColumnChooser is true || OrderControlled;

    // Any of the four opts in: Selectable for a grid that owns its selection, the other three for a caller
    // that owns it. Mirrors how SortControlled reads its three.
    private bool SelectionEnabled =>
        Selectable is true || SelectedKeys is not null || OnSelectionChange is not null
        || OnSelectionChangeAsync is not null;

    // Unlike Sort, "is it set?" is a sound signal here: an empty list is a perfectly good controlled selection
    // meaning "nothing picked", so null is unambiguous — the caller isn't controlling it.
    private bool SelectionControlled => SelectedKeys is not null;

    // The checkbox column, then the expander. Both HeaderCells/Cells/FooterCells and the detail row's colspan
    // read this — three call sites plus the colspan, which is exactly where a leading column goes wrong.
    private int LeadingCells => (SelectionEnabled ? 1 : 0) + (Expandable ? 1 : 0);

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
                if (columns[i].SortToken == Sort)
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
            var field = column >= 0 && column < columns.Count ? columns[column].SortToken : null;
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

    // The current selection as a set. Built once per render and threaded through the cells: a controlled
    // SelectedKeys is a list, and testing every page row against it with Contains would be O(page × selected)
    // on EVERY render — a 100-row page over a 10k select-all is a million comparisons to draw a checkbox.
    private HashSet<object> SelectionSet() =>
        SelectedKeys is null ? _selected : new HashSet<object>(SelectedKeys);

    // Takes the checkbox's reported state rather than flipping the current one. The client sends a checkbox's
    // actual `checked` ("true"/"false") precisely so the server can be self-correcting; a blind toggle would
    // drift from the DOM the moment a re-render lagged a click.
    private Task SetSelectedAsync(HashSet<object> selected, object key, bool on)
    {
        var next = new HashSet<object>(selected);
        if (on)
        {
            next.Add(key);
        }
        else
        {
            next.Remove(key);
        }

        return CommitSelectionAsync(next);
    }

    // Select-all covers THIS PAGE, because the page is all the grid holds: under TotalCount or an IQueryable it
    // has never seen the other rows and could not name their keys. Rows already picked on other pages are
    // untouched — that is what makes a selection survive paging.
    private Task SetPageSelectionAsync(HashSet<object> selected, IReadOnlyList<object> pageKeys, bool on)
    {
        var next = new HashSet<object>(selected);
        foreach (var key in pageKeys)
        {
            if (on)
            {
                next.Add(key);
            }
            else
            {
                next.Remove(key);
            }
        }

        return CommitSelectionAsync(next);
    }

    // pageKeys.Count > 0 is not redundant: All() over an empty page is vacuously true, which would render the
    // select-all box checked over nothing.
    private static bool AllSelected(HashSet<object> selected, IReadOnlyList<object> pageKeys) =>
        pageKeys.Count > 0 && pageKeys.All(selected.Contains);

    private Task CommitSelectionAsync(HashSet<object> next)
    {
        // Controlled: the caller owns it, so only report. Uncontrolled: keep it and report for good measure,
        // so a grid can track its own selection and still tell a toolbar about it.
        if (!SelectionControlled)
        {
            _selected.Clear();
            _selected.UnionWith(next);
        }

        return Raise(OnSelectionChange, OnSelectionChangeAsync, (IReadOnlyList<object>)next.ToList());
    }

    private void ToggleExpand(object key)
    {
        if (!_expanded.Add(key))
        {
            _expanded.Remove(key);
        }
    }

    // The grouped columns, outermost first. Resolves the field tokens against the columns and silently drops
    // anything that cannot band — an unknown token (a stale URL), a column that isn't Groupable, or one with
    // no name. A URL is user input: ?group=deleteMe must render an ungrouped grid, not throw.
    private List<BsColumn<T>> GroupColumns(IReadOnlyList<BsColumn<T>> columns)
    {
        var grouped = CurrentGrouped;
        var result = new List<BsColumn<T>>(grouped.Count);
        foreach (var token in grouped)
        {
            foreach (var column in columns)
            {
                if (column.Groupable && column.FieldName == token && !result.Contains(column))
                {
                    result.Add(column);
                    break;
                }
            }
        }

        return result;
    }

    // A grouped column is folded out of the table body: its value is identical for every row in its band and
    // already names the band header, so repeating it as a column would be a run of duplicates under a header
    // the band header already carries. Mirrors GroupColumns' match (Groupable + FieldName == token) so the two
    // never disagree about which columns are grouped. ShowGroupedColumns keeps the column instead.
    private bool IsGroupedAway(BsColumn<T> column) =>
        ShowGroupedColumns is not true
        && column.Groupable
        && column.FieldName is { } f
        && CurrentGrouped.Contains(f);

    // A column the chooser has hidden. Mirrors IsGroupedAway: token match against the current set, and a
    // column that opts out of hiding (Hideable = false) or has no name can never match.
    private bool IsHidden(BsColumn<T> column) =>
        column.Hideable && column.FieldName is { } f && CurrentHidden.Contains(f);

    // The columns that render, in render order, computed once per render (Render threads the result down).
    // Returns `columns` itself — same reference, no allocation — whenever nothing reorders, hides or folds,
    // which is every grid that uses none of these, so the feature stays free when unused.
    private IReadOnlyList<BsColumn<T>> VisibleColumns(IReadOnlyList<BsColumn<T>> columns)
    {
        var reorder = CurrentOrder.Count > 0;
        var hide = CurrentHidden.Count > 0;
        var fold = ShowGroupedColumns is not true && CurrentGrouped.Count > 0;

        // Grouping-only (or nothing): keep the original lazy fold that seeds on the first grouped-away column,
        // so a grouped grid allocates exactly as before and an ungrouped one not at all.
        if (!reorder && !hide)
        {
            if (!fold)
            {
                return columns;
            }

            List<BsColumn<T>>? visible = null;
            for (var i = 0; i < columns.Count; i++)
            {
                if (IsGroupedAway(columns[i]))
                {
                    visible ??= [.. columns.Take(i)]; // first hidden column: seed with the visible ones before it
                }
                else
                {
                    visible?.Add(columns[i]);
                }
            }

            return visible ?? columns;
        }

        // The chooser is in play. Reorder is a presentation permutation applied first, then drop the columns
        // that don't render.
        var shown = reorder ? Reordered(columns, CurrentOrder) : [.. columns];

        if (fold)
        {
            shown.RemoveAll(IsGroupedAway);
        }

        // Hiding must never empty the table by itself: a stale HiddenColumns that resolves to "hide everything"
        // is ignored wholesale (the same tolerance as a stale ?group=deleteMe). Folding may legitimately empty
        // it (group by every column), which the band/detail colspans already clamp to 1.
        if (hide && shown.Exists(c => !IsHidden(c)))
        {
            shown.RemoveAll(IsHidden);
        }

        return shown;
    }

    // Reorders `columns` to match `order` (field tokens, first to last). A movable column (Reorderable and
    // named) is ranked by its token's position in `order`, or last-among-movable in declared order when the
    // token is absent; unknown tokens are ignored (forgiving, like GroupColumns). A column that opts out of
    // reordering, or has no name to address it by, is a FIXTURE: it stays at its declared slot and the movable
    // columns flow into the gaps around it. Stable — equal ranks keep declared order.
    private static List<BsColumn<T>> Reordered(IReadOnlyList<BsColumn<T>> columns, IReadOnlyList<string> order)
    {
        var slots = new BsColumn<T>?[columns.Count];
        var movable = new List<(BsColumn<T> Column, int Rank, int Declared)>(columns.Count);

        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            if (column.Reorderable && column.FieldName is { } f)
            {
                var at = IndexOf(order, f);
                movable.Add((column, at < 0 ? int.MaxValue : at, i));
            }
            else
            {
                slots[i] = column; // fixture: anchored at its declared index
            }
        }

        movable.Sort(static (a, b) =>
            a.Rank != b.Rank ? a.Rank.CompareTo(b.Rank) : a.Declared.CompareTo(b.Declared));

        var result = new List<BsColumn<T>>(columns.Count);
        var m = 0;
        for (var i = 0; i < slots.Length; i++)
        {
            result.Add(slots[i] ?? movable[m++].Column);
        }

        return result;
    }

    private static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == value)
            {
                return i;
            }
        }

        return -1;
    }

    private Task SetGroupedAsync(IReadOnlyList<string> next)
    {
        if (!GroupControlled)
        {
            _grouped.Clear();
            _grouped.AddRange(next);
            // Collapse state is keyed by band values, which the new grouping invalidates wholesale: the old
            // paths address bands that no longer exist, and a stale one could collide with a new band's path.
            _collapsed.Clear();
        }

        return Raise(OnGroupedChange, OnGroupedChangeAsync, next);
    }

    // --- Column chooser & reorder --------------------------------------------------------------------------
    //
    // Two token lists, hidden and order, each following the same controlled/uncontrolled split as Grouped and
    // reusing the same drag field. The chooser menu drives them from real buttons; header drag is an
    // accelerator wired to the same DropOnHeaderAsync.

    private Task SetHiddenAsync(IReadOnlyList<string> next)
    {
        if (!HideControlled)
        {
            _hidden.Clear();
            _hidden.AddRange(next);
        }

        return Raise(OnHiddenColumnsChange, OnHiddenColumnsChangeAsync, next);
    }

    private Task SetOrderAsync(IReadOnlyList<string> next)
    {
        if (!OrderControlled)
        {
            _order.Clear();
            _order.AddRange(next);
        }

        return Raise(OnColumnOrderChange, OnColumnOrderChangeAsync, next);
    }

    // Showing a column can never empty the table; hiding one might, so it is refused when nothing would remain
    // visible. The last column's checkbox is disabled too — this is the keyboard and stale-input backstop for
    // the same rule.
    private Task ToggleHiddenAsync(string field)
    {
        var next = CurrentHidden.ToList();
        if (next.Remove(field))
        {
            return SetHiddenAsync(next);
        }

        var hiddenIfAdded = new HashSet<string>(next) { field };
        var remaining = 0;
        foreach (var column in Columns ?? [])
        {
            var hidden = column.Hideable && column.FieldName is { } f && hiddenIfAdded.Contains(f);
            if (!IsGroupedAway(column) && !hidden)
            {
                remaining++;
            }
        }

        if (remaining == 0)
        {
            return Task.CompletedTask;
        }

        next.Add(field);
        return SetHiddenAsync(next);
    }

    // The current display order as a COMPLETE token list — every reorderable, named column in the order it now
    // renders. ColumnOrder may be partial, so a move or drop materialises the whole order first and emits all
    // of it, which keeps the reported order stable rather than growing one token at a time.
    private List<string> EffectiveOrder()
    {
        var columns = Columns ?? [];
        var ordered = CurrentOrder.Count > 0 ? Reordered(columns, CurrentOrder) : columns;
        var result = new List<string>(ordered.Count);
        foreach (var column in ordered)
        {
            if (column.Reorderable && column.FieldName is { } f)
            {
                result.Add(f);
            }
        }

        return result;
    }

    // Moves a column one place earlier/later. Real buttons on the chooser row, so reorder is reachable without
    // a mouse — the same rule the group chips' move in/out follows.
    private Task MoveColumnAsync(string field, int delta)
    {
        var next = EffectiveOrder();
        var from = next.IndexOf(field);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= next.Count)
        {
            return Task.CompletedTask;
        }

        next.RemoveAt(from);
        next.Insert(to, field);
        return SetOrderAsync(next);
    }

    // Dropping a dragged header ON another header moves it before the target; a null target appends. Dropping a
    // header on itself is a no-op.
    private Task DropOnHeaderAsync(string? target)
    {
        var field = _dragField;
        _dragField = null;
        if (field is null || field == target)
        {
            return Task.CompletedTask;
        }

        var next = EffectiveOrder();
        next.Remove(field);
        var at = target is null ? next.Count : next.IndexOf(target);
        next.Insert(at < 0 ? next.Count : at, field);
        return SetOrderAsync(next);
    }

    // The band's identity: its key path from the outermost level in. Uses a unit separator so two levels
    // cannot be confused with one ("a" + "b|c" vs "a|b" + "c"), which would let unrelated bands share a
    // collapse entry.
    private static string BandPath(IReadOnlyList<object?> keys, int depth)
    {
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i <= depth; i++)
        {
            sb.Append(keys[i]).Append('');
        }

        return sb.ToString();
    }

    private void ToggleBand(string path)
    {
        if (!_collapsed.Add(path))
        {
            _collapsed.Remove(path);
        }
    }

    // --- Group panel -----------------------------------------------------------------------------------
    //
    // Drag state is the grid's own field rather than the DragDrop primitive. DragDrop renders no DOM and takes
    // a Body delegate, so using it would mean wrapping the grid's own output in it — and it sets
    // BypassRenderCache => true (it reads mutable drag state the framework cannot see through props), which
    // would re-execute the whole table's subtree on every render for the sake of a panel. One nullable string
    // is the whole state a group drag needs: the field being dragged. The client already does the hard parts —
    // preventDefault on dragover (which is what marks a drop target) and deduping the hover round-trip to one
    // message per element (rask-events.js).

    private Task GroupByAsync(string field)
    {
        var next = CurrentGrouped.ToList();
        if (!next.Remove(field))
        {
            next.Add(field);
        }

        return SetGroupedAsync(next);
    }

    private Task UngroupAsync(string field)
    {
        var next = CurrentGrouped.ToList();
        next.Remove(field);
        return SetGroupedAsync(next);
    }

    // Moves a level in or out one place. Nesting order is the grouping's meaning — region/rep and rep/region
    // are different reports — so it needs to be reachable without a mouse.
    private Task MoveGroupAsync(string field, int delta)
    {
        var next = CurrentGrouped.ToList();
        var from = next.IndexOf(field);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= next.Count)
        {
            return Task.CompletedTask;
        }

        next.RemoveAt(from);
        next.Insert(to, field);
        return SetGroupedAsync(next);
    }

    // Dropping ON a chip inserts before it; dropping on the panel's empty space appends.
    private Task DropOnAsync(string? target)
    {
        var field = _dragField;
        _dragField = null;
        if (field is null)
        {
            return Task.CompletedTask;
        }

        var next = CurrentGrouped.ToList();
        next.Remove(field);

        var at = target is null ? next.Count : next.IndexOf(target);
        next.Insert(at < 0 ? next.Count : at, field);
        return SetGroupedAsync(next);
    }

    // Dropping anywhere that is not the panel removes the level — the "drag it out to ungroup" gesture.
    private Task DropOutsideAsync()
    {
        var field = _dragField;
        _dragField = null;
        return field is null || !CurrentGrouped.Contains(field) ? Task.CompletedTask : UngroupAsync(field);
    }

    private Component GroupPanelRow(IReadOnlyList<BsColumn<T>> columns)
    {
        var grouped = GroupColumns(columns);

        return Div(
            Id: Id is null ? null : $"{Id}-grouppanel",
            Class: Bs.Join("bs-grid-grouppanel", Display.Flex(), Flex.Align(BsAlign.Center), "gap-2",
                Margin.Bottom(2)),
            // The drop target for "group by this" and for reordering. OnDragOver is what the client turns into
            // preventDefault, and without it the browser rejects the drop outright.
            OnDragOver: () => { },
            OnDropAsync: () => DropOnAsync(null))[GroupPanelItems(grouped)];
    }

    private IEnumerable<Component?> GroupPanelItems(List<BsColumn<T>> grouped)
    {
        yield return Span(Class: Bs.Join(Txt.Color(BsColor.Secondary), Font.Small))[
            grouped.Count == 0 ? "Drag a column here to group by it" : "Grouped by"];

        foreach (var column in grouped)
        {
            yield return GroupChip(column, grouped);
        }
    }

    private Component GroupChip(BsColumn<T> column, List<BsColumn<T>> grouped)
    {
        var field = column.FieldName!;
        var at = grouped.IndexOf(column);

        return Div(
            Key: $"chip:{field}",
            Class: Bs.Join("bs-grid-chip", Display.Flex(), Flex.Align(BsAlign.Center), "gap-1",
                "badge text-bg-secondary"),
            Draggable: true,
            OnDragStart: () => _dragField = field,
            // dragend fires after any drop, so this is the "drag the chip out to ungroup" gesture: dropping on
            // the panel or another chip already consumed _dragField (DropOnAsync nulled it), leaving this a
            // no-op; dropping on nothing leaves it set, and DropOutsideAsync removes the level.
            OnDragEndAsync: () => DropOutsideAsync(),
            OnDragOver: () => { },
            OnDropAsync: () => DropOnAsync(field))[
            Span()[column.Title],
            ChipButton(BsIconName.ChevronLeft, $"Move {column.Title} out one level", at == 0,
                () => MoveGroupAsync(field, -1)),
            ChipButton(BsIconName.ChevronRight, $"Move {column.Title} in one level", at == grouped.Count - 1,
                () => MoveGroupAsync(field, 1)),
            ChipButton(BsIconName.X, $"Stop grouping by {column.Title}", false, () => UngroupAsync(field))
        ];
    }

    // A real <button> per action — this is what makes the panel keyboard-operable rather than drag-only.
    private static Component ChipButton(BsIconName icon, string label, bool disabled, Func<Task> onClick) =>
        Button(
            Type: "button",
            Class: Bs.Join("btn btn-sm btn-link text-reset text-decoration-none", Padding.All(0), "lh-1"),
            Disabled: disabled ? true : null,
            Aria: new Dictionary<string, string?> { ["label"] = label },
            OnClickAsync: () => onClick())[BsIcon(Name: icon)];

    // --- Column chooser menu -------------------------------------------------------------------------------
    //
    // A disclosure button plus, when open, a checkbox-and-reorder row per named column. It is the keyboard route
    // to both axes: the checkbox shows/hides, the up/down buttons reorder. Header drag is only an accelerator on
    // top of the same handlers. The whole thing renders only when ColumnChooser is true, so a grid that drives
    // HiddenColumns/ColumnOrder from the URL alone (no menu) pays nothing for it.
    // The toolbar fragment when the chooser is on: the Columns menu first, then the group panel if it too is on.
    // A fragment renders its children with no wrapper, so a null group panel adds nothing.
    private Component Toolbar(IReadOnlyList<BsColumn<T>> columns) =>
        [ColumnChooserBar(columns), GroupPanel is true ? GroupPanelRow(columns) : null];

    private Component ColumnChooserBar(IReadOnlyList<BsColumn<T>> columns) =>
        Div(
            Id: Id is null ? null : $"{Id}-columnchooser",
            Class: Bs.Join("bs-grid-columnchooser", Position.Relative, Margin.Bottom(2)))[
            Button(
                Type: "button",
                Class: "btn btn-sm btn-outline-secondary",
                Aria: new Dictionary<string, string?>
                {
                    ["expanded"] = _chooserOpen ? "true" : "false",
                    ["label"] = "Columns",
                },
                OnClick: () => _chooserOpen = !_chooserOpen)[
                BsIcon(Name: BsIconName.Columns, Class: Margin.End(1)), "Columns"],
            _chooserOpen
                ? Div(Class: Bs.Join("bs-grid-columnmenu", "list-group", Margin.Top(1)))[
                    ColumnChooserItems(columns)]
                : null
        ];

    private IEnumerable<Component> ColumnChooserItems(IReadOnlyList<BsColumn<T>> columns)
    {
        // List the columns in the order they render, so the up/down buttons read the way the table looks. Hidden
        // columns are listed too (that is how you get them back), so the walk is over the full named set rather
        // than `visible`.
        var display = CurrentOrder.Count > 0 ? Reordered(columns, CurrentOrder) : columns;
        var order = EffectiveOrder();
        var visibleCount = 0;
        foreach (var column in columns)
        {
            if (!IsGroupedAway(column) && !IsHidden(column))
            {
                visibleCount++;
            }
        }

        foreach (var column in display)
        {
            if (column.FieldName is not { } field)
            {
                continue;
            }

            var visible = !IsHidden(column);

            // A non-hideable column is always shown; the last visible column can't be unchecked into a bodyless
            // table. Either way the box is locked on.
            var lockOn = !column.Hideable || (visible && visibleCount == 1);

            // onChange is built HERE, not inside SelectBox, so its closure captures the grid — the owner the
            // framework re-renders after a change. See the remarks on SelectBox.
            var box = SelectBox(visible, new Dictionary<string, string?> { ["label"] = $"Show {column.Title}" },
                lockOn, _ => ToggleHiddenAsync(field));

            var rank = order.IndexOf(field);
            var canMove = column.Reorderable && rank >= 0;

            yield return Div(
                Key: $"col:{field}",
                Class: Bs.Join("bs-grid-columnitem", "list-group-item", Display.Flex(), Flex.Align(BsAlign.Center),
                    "gap-2", Padding.Y(1)))[
                box,
                Span(Class: "me-auto")[column.Title],
                canMove
                    ? ChipButton(BsIconName.ChevronUp, $"Move {column.Title} earlier", rank == 0,
                        () => MoveColumnAsync(field, -1))
                    : null,
                canMove
                    ? ChipButton(BsIconName.ChevronDown, $"Move {column.Title} later", rank == order.Count - 1,
                        () => MoveColumnAsync(field, 1))
                    : null
            ];
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

        // The toolbar strip above the grid: the Columns menu and/or the group panel, whichever are on. When
        // ColumnChooser is off it is exactly what it was before — null, or the group panel Div directly — so the
        // existing paths stay byte-identical; only a chooser grid gets the enclosing fragment.
        var toolbar = ColumnChooser is true
            ? Toolbar(columns)
            : GroupPanel is true ? GroupPanelRow(columns) : null;

        // A fetch in flight is not "no results": without this guard the first load flashes the placeholder
        // before the rows land, and every refetch of an empty filter blinks it back.
        if (total == 0 && Empty is not null && !Busy)
        {
            // The toolbar stays with the empty state: it is how the user got here (grouped or hidden down to
            // nothing) and how they get back out.
            return Wrap(toolbar, Empty, null);
        }

        // The columns that actually render — reordered, then hidden and grouped-away folded out — computed once
        // per render and threaded down like `selected`/`pageKeys` below, so the per-cell path never re-derives
        // it. A folded column takes its footer with it, so `visible` also decides whether there is a <tfoot>.
        // HeaderCells renders `visible` (in its order) but also takes the FULL `columns`, because the sort index
        // is a position in `columns` that a reordered/filtered subset would renumber.
        var visible = VisibleColumns(columns);
        var hasFooter = visible.Any(c => c.HasFooter);

        // Built once per render and threaded down, rather than rebuilt per row. The page's keys come with it:
        // the select-all box needs them, and computing a key is a user delegate call per row.
        var selected = SelectionEnabled ? SelectionSet() : null;
        var pageKeys = selected is null ? [] : PageKeys(pageRows);

        var table = BsTable(Id: Id, Striped: Striped, Hover: Hover, Small: Small, Responsive: Responsive,
            StickyHeader: StickyHeader, MaxHeight: MaxHeight, Class: Class,
            Aria: Busy ? BsGridAria.Busy : null)[
            Thead()[Tr()[HeaderCells(visible, columns, selected, pageKeys)]],
            Tbody()[BodyRows(visible, columns, pageRows, selected)],
            hasFooter ? Tfoot()[Tr()[FooterCells(visible, footerRows)]] : null
        ];

        return Wrap(toolbar, table,
            PageSize > 0 && pageCount > 1 ? Pager(pageCount, total) : null);
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
    private Component Wrap(Component? panel, Component? content, Component? pager) =>
        Loading is null
            ? [panel, content, pager]
            : Div(Class: Position.Relative)[panel, content, pager, Busy ? Overlay() : null];

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
        var sortBy = sortColumn >= 0 && sortColumn < columns.Count ? columns[sortColumn].OrderBy : null;
        var groups = GroupColumns(columns);
        var page = Math.Max(0, CurrentPage);
        var key = (query, sortBy, page, CurrentSortDescending, PageSize, string.Join(',', CurrentGrouped));

        if (_queryCache is { } cached && cached.Key == key)
        {
            return (cached.Rows, cached.Rows, cached.Total, cached.PageCount);
        }

        // OrderBy<T, object?> is called with the expression as-is: no MakeGenericMethod, so nothing here is a
        // trimming or AOT hazard. The boxing convert in the expression is a no-op that providers see through.
        // Unsorted, the caller's own ordering stands — which is why an IQueryable Data wants to arrive
        // ordered: Skip/Take over an unordered query is undefined in SQL. A sort replaces it, not adds to it.
        //
        // The GROUP columns lead the ORDER BY, because a band is a run of consecutive rows: without ordering by
        // them first the store would interleave the categories and the same band would repeat down the page.
        // The user's sort then applies WITHIN each band, which is what makes "group by category, sort by price"
        // mean what it looks like. A groupable column needs an expression to order by here — the same rule
        // Sortable already follows in this mode — so one without is skipped rather than silently dropping the
        // ordering the bands depend on.
        IOrderedQueryable<T>? ordered = null;
        foreach (var g in groups)
        {
            if (g.OrderBy is not { } expr)
            {
                continue;
            }

            ordered = ordered is null ? query.OrderBy(expr) : ordered.ThenBy(expr);
        }

        if (sortBy is not null)
        {
            ordered = ordered is null
                ? CurrentSortDescending ? query.OrderByDescending(sortBy) : query.OrderBy(sortBy)
                : CurrentSortDescending ? ordered.ThenByDescending(sortBy) : ordered.ThenBy(sortBy);
        }

        IQueryable<T> source = ordered ?? query;

        var total = query.Count();
        var pageCount = PageSize > 0 ? Math.Max(1, (total + PageSize - 1) / PageSize) : 1;
        page = Math.Min(page, pageCount - 1);

        var rows = (PageSize > 0 ? source.Skip(page * PageSize).Take(PageSize) : source).ToList();

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
        var sorted = sortColumn >= 0 && sortColumn < columns.Count && columns[sortColumn].Sortable
            ? columns[sortColumn]
            : null;

        // The GROUP keys lead, then the user's sort applies WITHIN each band. Here the grid holds every row, so
        // it can guarantee whole bands outright: a band is a run of consecutive rows, and ordering by the group
        // keys first is what makes the runs contiguous instead of scattering each category down the page.
        // Sorting first and banding after would fragment every band — which is why this is not "sort, then
        // group" but one composed ordering.
        IOrderedEnumerable<T>? ordered = null;
        foreach (var g in GroupColumns(columns))
        {
            ordered = ordered is null ? data.OrderBy(g.GroupSort) : ordered.ThenBy(g.GroupSort);
        }

        if (sorted is not null)
        {
            ordered = ordered is null
                ? CurrentSortDescending ? data.OrderByDescending(sorted.Sort) : data.OrderBy(sorted.Sort)
                : CurrentSortDescending ? ordered.ThenByDescending(sorted.Sort) : ordered.ThenBy(sorted.Sort);
        }

        if (ordered is not null)
        {
            view = ordered;
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

    // The band header: one full-width cell in the same <tbody> as the rows. .table-group-divider draws the
    // rule Bootstrap already ships for exactly this.
    //
    // A collapsible band's toggle carries aria-expanded but deliberately NO aria-controls. The content it
    // controls is a run of sibling <tr>s with no wrapper element to point at, and aria-controls takes an id
    // LIST — so honouring it would mean minting and emitting an id for every row in every band. aria-expanded
    // alone is a valid disclosure pattern; this is the price of banding inside one <tbody>.
    private Component BandHeaderRow(IReadOnlyList<BsColumn<T>> visible, BsColumn<T> column, object? key,
        IReadOnlyList<T> band, int level, string path, bool collapsed)
    {
        Component content = column.GroupHeader is { } header
            ? header(key, band)
            : [
                Span(Class: Font.Semibold)[$"{column.Title}: {key}"],
                Span(Class: Bs.Join(Txt.Color(BsColor.Secondary), Margin.Start(2), Font.Small))[
                    $"({band.Count})"],
            ];

        var label = GroupCollapsible is true
            ? BsButton(
                Color: BsColor.Secondary, Outline: true, Size: BsSize.Sm, Class: Margin.End(2),
                Aria: new Dictionary<string, string?>
                {
                    ["expanded"] = collapsed ? "false" : "true",
                    ["label"] = $"Toggle {column.Title} {key}",
                },
                OnClick: () => ToggleBand(path))[
                BsIcon(Name: collapsed ? BsIconName.ChevronRight : BsIconName.ChevronDown)]
            : null;

        // Nested bands indent so the hierarchy is visible; level 0 sits flush.
        return Tr(Key: $"band:{path}", Class: "table-group-divider")[
            Td(Colspan: Math.Max(1, visible.Count + LeadingCells),
                Class: level > 0 ? Padding.Start(level * 3 + 2) : null)[label, content]
        ];
    }

    // Reuses each column's Footer/FooterTemplate over the band's rows: those delegates already take an
    // IReadOnlyList<T>, so a subtotal is the same delegate over a narrower set — one shape to learn, and a
    // column that totals in the footer totals per band for free.
    private Component SubtotalRow(IReadOnlyList<BsColumn<T>> visible, IReadOnlyList<T> band, int level,
        string path) =>
        Tr(Key: $"sub:{path}", Class: "table-light")[SubtotalCells(visible, band, level)];

    private IEnumerable<Component> SubtotalCells(IReadOnlyList<BsColumn<T>> visible, IReadOnlyList<T> band,
        int level)
    {
        for (var i = 0; i < LeadingCells; i++)
        {
            yield return Td()[""];
        }

        // `visible` has the grouped-away columns already folded out, so the "Subtotal" caption on the first cell
        // with no footer of its own lands on the first column the user actually sees.
        for (var c = 0; c < visible.Count; c++)
        {
            var column = visible[c];
            yield return Td(Class: Bs.Join(column.Class, Font.Semibold))[
                c == 0 && !column.HasFooter ? "Subtotal" : column.FooterCell(band)];
        }
    }

    // A row's key. RowKey is what makes selection and expansion track the ROW; without one this is the row's
    // index on the page, which is why RASK033 asks for a RowKey as soon as either feature is on.
    private object KeyOf(T row, int index) => RowKey?.Invoke(row) ?? index;

    private IReadOnlyList<object> PageKeys(IReadOnlyList<T> pageRows)
    {
        var keys = new object[pageRows.Count];
        for (var i = 0; i < pageRows.Count; i++)
        {
            keys[i] = KeyOf(pageRows[i], i);
        }

        return keys;
    }

    // `visible` is the columns that render, already reordered with grouped-away/hidden folded out; `columns` is
    // the full declared list, needed only to resolve each header's sort index (see OriginalIndex).
    private IEnumerable<Component> HeaderCells(IReadOnlyList<BsColumn<T>> visible,
        IReadOnlyList<BsColumn<T>> columns, HashSet<object>? selected, IReadOnlyList<object> pageKeys)
    {
        if (selected is not null)
        {
            yield return Th(Class: "bs-grid-check", Scope: "col")[
                // "Select all" would be a lie next to a pager: the grid can only reach this page. The client
                // reports the box's real `checked` as "true"/"false" rather than a toggle signal, so the
                // server stays self-correcting even if a re-render lags a click.
                SelectBox(AllSelected(selected, pageKeys), BsGridAria.SelectPage,
                    Busy || pageKeys.Count == 0,
                    raw => SetPageSelectionAsync(selected, pageKeys, raw == "true"))
            ];
        }

        if (Expandable)
        {
            yield return Th(Scope: "col")[""];
        }

        foreach (var column in visible)
        {
            var field = column.FieldName;

            // The panel's other half: the keyboard route to grouping. Dragging the header does the same for a
            // mouse — and the same drag also reorders. Which one a drop means is decided by where it lands
            // (a panel/chip → group; another header → reorder), so both share the grid's one drag field.
            var canGroup = GroupPanel is true && column.Groupable && field is not null;
            var canReorder = ReorderEnabled && column.Reorderable && field is not null;
            var groupBtn = canGroup ? GroupByButton(column) : null;
            var drag = canGroup || canReorder ? field : null;

            // A sort the grid cannot actually perform must not advertise a control: a controlled sort is
            // reported by a name (SortField, or the one read off Field), and a Query is ordered by an
            // expression (SortBy, or Field itself). Missing either, the header stays plain.
            if (!column.Sortable
                || (SortControlled && column.SortToken is null)
                || (Data is IQueryable<T> && column.OrderBy is null))
            {
                yield return Th(Class: column.Class, Scope: "col",
                    Draggable: drag is not null ? true : null,
                    OnDragStart: drag is not null ? () => _dragField = drag : null,
                    OnDragEnd: drag is not null ? () => _dragField = null : null,
                    OnDragOver: canReorder ? () => { }
                : null,
                    OnDropAsync: canReorder ? () => DropOnHeaderAsync(field) : null)[column.Title, groupBtn];
                continue;
            }

            // Reorder/hide give the header a different sequence than `columns`, but sort tracks a column's
            // IDENTITY, not its slot — so resolve the index into the full list rather than the render position.
            var index = OriginalIndex(columns, column);
            var sorted = CurrentSortColumn == index;
            var caret = sorted
                ? BsIcon(Name: CurrentSortDescending ? BsIconName.CaretDownFill : BsIconName.CaretUpFill,
                    Class: Margin.Start(1))
                : null;

            // aria-sort advertises the direction to screen readers. The control is a real <button>, so
            // keyboard focus and Enter/Space work with no JS — but Type must be explicit, because <button>
            // defaults to type=submit and a grid inside a <form> would otherwise submit it on every sort.
            yield return Th(Class: column.Class, Scope: "col", Aria: BsGridAria.Sort(sorted, CurrentSortDescending),
                Draggable: drag is not null ? true : null,
                OnDragStart: drag is not null ? () => _dragField = drag : null,
                OnDragEnd: drag is not null ? () => _dragField = null : null,
                OnDragOver: canReorder ? () => { }
            : null,
                OnDropAsync: canReorder ? () => DropOnHeaderAsync(field) : null)[
                Button(
                    Type: "button",
                    Class: Bs.Join("btn btn-sm btn-link text-decoration-none", Padding.All(0), Font.Semibold),
                    Aria: Busy ? BsGridAria.Disabled : null,
                    OnClickAsync: () => ToggleSortAsync(index))[column.Title, caret],
                groupBtn
            ];
        }
    }

    // A column's position in the full, declared `columns` list — the index the sort state
    // (CurrentSortColumn/ToggleSortAsync) speaks. Resolved by reference because reorder and hide give the
    // header a different sequence; columns are unique instances, and the list is short.
    private static int OriginalIndex(IReadOnlyList<BsColumn<T>> columns, BsColumn<T> column)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            if (ReferenceEquals(columns[i], column))
            {
                return i;
            }
        }

        return -1;
    }

    // Toggles this column in and out of the grouping. A separate control from the sort button on purpose: the
    // header's click already means "sort", and overloading it would make grouping unreachable on a column that
    // is sortable — or sorting unreachable on one that is groupable.
    private Component GroupByButton(BsColumn<T> column)
    {
        var field = column.FieldName!;
        var on = CurrentGrouped.Contains(field);

        return Button(
            Type: "button",
            Class: Bs.Join("btn btn-sm btn-link text-decoration-none", Padding.All(0), Margin.Start(1),
                on ? Txt.Color(BsColor.Primary) : Txt.Color(BsColor.Secondary)),
            Aria: new Dictionary<string, string?>
            {
                ["pressed"] = on ? "true" : "false",
                ["label"] = on ? $"Stop grouping by {column.Title}" : $"Group by {column.Title}",
            },
            OnClickAsync: () => GroupByAsync(field))[BsIcon(Name: BsIconName.Diagram3)];
    }

    // `visible` is the columns that render (grouped-away ones already folded out); `columns` is the full list,
    // needed only to resolve the grouping columns for banding — a band's key comes from a column that has itself
    // been folded away.
    private IEnumerable<Component> BodyRows(IReadOnlyList<BsColumn<T>> visible,
        IReadOnlyList<BsColumn<T>> columns, IReadOnlyList<T> pageRows, HashSet<object>? selected)
    {
        var groups = GroupColumns(columns);
        return groups.Count == 0
            ? PlainRows(visible, pageRows, selected, 0, pageRows.Count)
            : BandRows(visible, pageRows, selected, groups, 0, 0, pageRows.Count);
    }

    // Bands one level of the page, then recurses. A band is a RUN of consecutive rows sharing the key at this
    // level — which is sound only because Local/Queried ordered by the group keys first (see there). Rows
    // arriving unordered would simply produce repeated bands: visible and self-explaining, never silent.
    //
    // Nesting falls out of the recursion: each band at level N re-bands its own slice by level N+1, and the
    // deepest level renders the rows themselves.
    private IEnumerable<Component> BandRows(IReadOnlyList<BsColumn<T>> visible, IReadOnlyList<T> pageRows,
        HashSet<object>? selected, List<BsColumn<T>> groups, int level, int start, int end)
    {
        var column = groups[level];
        var i = start;
        while (i < end)
        {
            var key = column.Group(pageRows[i]);

            // Extend the run while the key holds. Equals, not ==: the keys are boxed object?, where == would
            // compare references and band every string separately.
            var runEnd = i + 1;
            while (runEnd < end && Equals(column.Group(pageRows[runEnd]), key))
            {
                runEnd++;
            }

            var path = BandPath(pageRows[i], groups, level);
            var band = Slice(pageRows, i, runEnd);
            var collapsed = _collapsed.Contains(path);

            yield return BandHeaderRow(visible, column, key, band, level, path, collapsed);

            if (!collapsed)
            {
                var inner = level + 1 < groups.Count
                    ? BandRows(visible, pageRows, selected, groups, level + 1, i, runEnd)
                    : PlainRows(visible, pageRows, selected, i, runEnd);

                foreach (var row in inner)
                {
                    yield return row;
                }

                // Subtotals sit at the END of the band, where a running total belongs, and only at the deepest
                // level: one per innermost band rather than a cascade of identical rows at every level.
                if (GroupSubtotals is true && level + 1 == groups.Count && visible.Any(c => c.HasFooter))
                {
                    yield return SubtotalRow(visible, band, level, path);
                }
            }

            i = runEnd;
        }
    }

    // A window over the page without copying it per band — the band's rows are contiguous by construction, and
    // Footer/GroupHeader only read them.
    private static IReadOnlyList<T> Slice(IReadOnlyList<T> rows, int start, int end)
    {
        var band = new T[end - start];
        for (var i = 0; i < band.Length; i++)
        {
            band[i] = rows[start + i];
        }

        return band;
    }

    private string BandPath(T row, List<BsColumn<T>> groups, int level)
    {
        var keys = new object?[level + 1];
        for (var i = 0; i <= level; i++)
        {
            keys[i] = groups[i].Group(row);
        }

        return BandPath(keys, level);
    }

    private IEnumerable<Component> PlainRows(IReadOnlyList<BsColumn<T>> visible, IReadOnlyList<T> pageRows,
        HashSet<object>? selected, int start, int end)
    {
        for (var r = start; r < end; r++)
        {
            var row = pageRows[r];
            var key = KeyOf(row, r);
            var isSelected = selected?.Contains(key) is true;

            // table-active is joined with the caller's RowClass rather than replacing it, so a row can be both
            // overdue and selected.
            yield return Tr(Key: key, Class: BsClass.Join(RowClass?.Invoke(row), isSelected ? "table-active" : null))[
                Cells(visible, row, key, r, selected, isSelected)];

            if (!Expandable || !_expanded.Contains(key))
            {
                continue;
            }

            var detail = ExpandedContent!(row);
            if (detail is not null)
            {
                yield return Tr(Key: $"{key}:detail", Id: DetailId(r))[
                    Td(Colspan: Math.Max(1, visible.Count + LeadingCells))[detail]
                ];
            }
        }
    }

    private IEnumerable<Component> Cells(IReadOnlyList<BsColumn<T>> visible, T row, object key, int index,
        HashSet<object>? selected, bool isSelected)
    {
        if (selected is not null)
        {
            // The checkbox has no visible label, so aria-label is its only accessible name. It names the ROW
            // (via the first Value column where there is one), because twenty identical "Select row"s in a
            // list read as one control repeated rather than twenty distinct ones.
            yield return Td(Class: "bs-grid-check")[
                SelectBox(isSelected, SelectRowAria(visible, row), Busy,
                    raw => SetSelectedAsync(selected, key, raw == "true"))
            ];
        }

        if (Expandable)
        {
            yield return Td()[ExpanderButton(key, index)];
        }

        // Built once and shared by every clickable cell in this row. The callback is per-row, so minting a
        // delegate per cell would multiply the closure allocations by the column count for no benefit —
        // the handler *id* is still per element, which is why OnRowClick scales rows × columns.
        var click = RowClickHandler(row);

        // `visible` already excludes grouped-away columns, so the row loop stays a straight walk — no per-cell
        // hidden check on the hottest path.
        foreach (var column in visible)
        {
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

    // The bare selection checkbox.
    //
    // Deliberately the core Input rather than BsCheck, which is the Bs primitive this library reaches for
    // everywhere else. BsCheck is a *form field*: it renders a .form-check wrapper sized for a label, resolves
    // a binding context, and registers as a child component. A selection box is none of those things — it has
    // no label, no binding and no validation — so all of that would be paid 101 times on a 100-row grid to
    // then be undone in CSS (the wrapper's label padding would push the box off-centre in the cell).
    // Measured: BsCheck cost +82% render allocation over a plain grid, the raw input a fraction of that.
    // Everything BsCheck would have contributed here is one class name.
    // onChange is built by the CALLER, not from a Func wrapped here, and that is load-bearing. The handler's
    // owner — the component dirty-marked after it runs — is resolved by unwrapping the closure's captured
    // `this` (DelegateOwner.Resolve). A lambda created inside this static helper would capture only its
    // parameter, so its display class has no captured `this`, the grid would never be found, and the
    // selection would change without anything re-rendering. Built at the call site it captures the grid, and
    // the owner resolves. (BsCheck sidesteps this with an explicit consumer.StateHasChanged(); a raw Element
    // has no such machinery — see the remarks on IFormControl.ControlledChangeHandler.)
    private static Component SelectBox(bool selected, IReadOnlyDictionary<string, string?> aria, bool disabled,
        CallbackAsync<string> onChange) =>
        Input<string>(
            Type: InputType.Checkbox,
            Class: "form-check-input",
            Checked: selected,
            // A real `disabled`, not aria-disabled: unlike the sort/pager controls (kept focusable so a fetch
            // doesn't throw away the user's keyboard position), a box that cannot be changed shouldn't be
            // reachable at all.
            Disabled: disabled ? true : null,
            Aria: aria,
            OnChangeAsync: onChange);

    // Names the row's checkbox from its first Value column ("Select Espresso Machine"). Falls back to a plain
    // label when every column is a Template and there is no text to borrow.
    private static IReadOnlyDictionary<string, string?> SelectRowAria(IReadOnlyList<BsColumn<T>> columns, T row)
    {
        foreach (var column in columns)
        {
            if (column.Value?.Invoke(row)?.ToString() is { Length: > 0 } label)
            {
                return new Dictionary<string, string?> { ["label"] = $"Select {label}" };
            }
        }

        return BsGridAria.SelectRow;
    }

    private IEnumerable<Component> FooterCells(IReadOnlyList<BsColumn<T>> visible, IReadOnlyList<T> data)
    {
        // The easy miss: the footer needs a leading cell for EVERY leading column, or every <tfoot> cell is
        // off by one and the totals sit under the wrong headers.
        if (SelectionEnabled)
        {
            yield return Td()[""];
        }

        if (Expandable)
        {
            yield return Td()[""];
        }

        // `visible` has grouped-away columns already folded out, so each remaining column lines up with its
        // header.
        foreach (var column in visible)
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
        // The arrows' only child is a decorative (aria-hidden) BsIcon, so they need an explicit
        // accessible name — without it a screen reader announces an unlabelled button.
        yield return BsPageItem(Key: "prev", Disabled: Busy || CurrentPage == 0,
            Aria: new Dictionary<string, string?> { ["label"] = "Previous page" },
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
            Aria: new Dictionary<string, string?> { ["label"] = "Next page" },
            OnClickAsync: () => GoToPageAsync(CurrentPage + 1, pageCount))[BsIcon(Name: BsIconName.ChevronRight)];
    }

    // Keyed by the row's position, not by RowKey: a RowKey is arbitrary user data ("Espresso Machine", a Guid,
    // a composite) and interpolating it produces ids containing spaces. aria-controls is a SPACE-SEPARATED id
    // list, so one space silently turns the reference into two tokens that match nothing — breaking exactly the
    // association it exists to make. The index is always id-safe, and it only has to be unique and resolvable
    // within the render that emits it, which it is. The instance id keeps two grids on one page apart.
    private string DetailId(int index) => $"{Id ?? $"bsgrid{_instanceId}"}-detail-{index}";
}
