using System.Globalization;
using System.Linq.Expressions;
using Rask.Core.DragAndDrop;
using Rask.Core.Routing;

namespace Rask.Ui;

/// <summary>
/// A table over a typed row sequence: sortable headers, paging, selection, expandable detail rows,
/// grouping, a column chooser, and a card layout on a phone.
/// </summary>
/// <remarks>
/// <para>
/// <b>Columns are the chain's children, and they arrive through a factory:</b>
/// <c>UiDataGrid.Data(_products)[c =&gt; [ c.Field(p =&gt; p.Name).Title("Product").Sortable(true) ]]</c>.
/// The lambda's parameter is the grid, which is what fixes the row type — a column written as a flat
/// child has nothing to infer its own lambda from and does not compile. See the grid's column indexer.
/// </para>
/// <para>
/// <b>Where the sorting and paging happen is decided by what it is given.</b>
/// <list type="bullet">
///     <item>
///         A list — in memory. Right for a set small enough to hold, and the only mode that needs
///         nothing of the caller.
///     </item>
///     <item>
///         An <see cref="IQueryable{T}" /> handed to the same <see cref="Data" /> step — in the store.
///         The grid translates the sort into <c>ORDER BY</c> and the page into <c>Skip</c>/<c>Take</c>,
///         so the set can be arbitrarily large. One step rather than two, because an
///         <see cref="IQueryable{T}" /> IS an <see cref="IEnumerable{T}" /> and a second name for the
///         same slot is a second thing to get wrong. It enumerates synchronously, which is why the third
///         mode exists.
///     </item>
///     <item>
///         A <see cref="Source" /> — an awaited page. The grid hands it the sort and the page it wants
///         and takes back the rows plus a total, so the fetch is yours and the await is real.
///     </item>
/// </list>
/// </para>
/// <para>
/// <b>Every state axis is controlled or uncontrolled, one axis at a time.</b> Say nothing and the grid
/// holds its own sort, page, selection, grouping and column layout in fields and redraws through the
/// live diff. Name the state and its change callback — <c>Page</c> with <c>OnPageChange</c> — and that
/// axis belongs to the page instead, while the others carry on holding their own.
/// </para>
/// <para>
/// <b>It needs the runtime.</b> Sorting, paging and every other interaction are C# handlers, so a grid
/// on a page that has not booted renders its first page and stays there. That is the deliberate trade —
/// see the kit's "who owns the state".
/// </para>
/// </remarks>
/// <typeparam name="T">The row type.</typeparam>
/// <typeparam name="TKey">What identifies one row, as returned by <see cref="RowKey" />.</typeparam>
public sealed partial class UiDataGrid<T, TKey> : Component
    where TKey : notnull
{
    // Per-instance, so two id-less grids on one page cannot collide on the ids their detail rows are
    // announced by — aria-controls points at them, and a collision aims it at the wrong row.
    private static int _instances;

    private readonly int _instance = Interlocked.Increment(ref _instances);

    // Uncontrolled state. Each of these is consulted only while the matching controlled property is
    // unset, which is what lets one axis be driven from outside while the rest keep holding their own.
    private readonly HashSet<object> _expanded = [];
    private readonly HashSet<string> _collapsed = [];
    private readonly List<string> _grouped = [];
    private readonly List<string> _hidden = [];
    private readonly List<string> _order = [];
    private bool _chooserOpen;
    private int _page;
    private string? _sortField;
    private bool _sortDescending;

    // The column factory. It was kept as a Delegate because the IColumnHost interface could not name
    // this type without becoming generic itself; the indexer is declared right here on the grid now, so
    // the field could be typed — it stays a Delegate only because nothing reads it in a typed way and
    // widening it would cost an allocation at every call.
    private Delegate? _columnFactory;

    // The UNCONTROLLED selection. Typed in the key rather than in `object`, so a membership test per row
    // per render boxes nothing, and a field rather than state rebuilt per render because it is the one
    // piece of the selection that has to survive one.
    //
    // It used to live in a UiGridKeys<T, TKey> strategy hanging off an untyped UiGridKeys<T> base, and
    // that whole arrangement existed for one reason: the grid was UiDataGrid<T> and could not name TKey.
    // It can now, so there is nothing left for the strategy to hide.
    private readonly HashSet<TKey> _own = [];

    // The last page an async Source handed back, and what was asked for to get it. Compared rather than
    // re-fetched: Render runs far more often than the request changes.
    private UiGridPage<T>? _fetched;
    private UiGridRequest? _fetchedFor;

    /// <summary>Reads a row's identity — <c>p =&gt; p.Id</c>.</summary>
    /// <remarks>
    ///     <para>
    ///         Required, and the type argument it pins is what the selection is expressed in:
    ///         <see cref="Selected" /> is an <c>IReadOnlyList&lt;TKey&gt;</c> of exactly these.
    ///     </para>
    ///     <para>
    ///         Not <c>Key</c>: that is already the chain's step for reconciliation identity — which
    ///         instance of the GRID is being built — and has to be able to come first (RASK046). This one
    ///         is about the rows inside it.
    ///     </para>
    ///     <para>
    ///         It is required rather than optional because the alternative was worse than it looked. The
    ///         key used to reach the chain through a hand-written <c>RowKey&lt;TRow, TKey&gt;</c> step, and
    ///         a grid that never took it still rendered: rows fell back to their INDEX for identity, so the
    ///         live diff reordered by position, and the selection steps — which were reachable regardless —
    ///         fabricated a strategy with no selector in it. Naming the key is now the only way to build a
    ///         grid at all.
    ///     </para>
    /// </remarks>
    public required Func<T, TKey> RowKey { get; set; }

    /// <summary>The selected rows, by key. Setting it hands selection to the parent.</summary>
    public IReadOnlyList<TKey>? Selected { get; set; }

    /// <summary>Called with the selection after the reader changed it.</summary>
    public Callback<IReadOnlyList<TKey>>? OnSelectionChange { get; set; }

    /// <summary>The rows, in memory or as a query.</summary>
    /// <remarks>
    ///     An <see cref="IQueryable{T}" /> is ordered and paged in the STORE — the grid calls
    ///     <c>OrderBy</c>, <c>Skip</c> and <c>Take</c> on it and counts with <c>Count()</c>, so a column
    ///     that wants to be sorted there needs a <see cref="UiColumn{T}.Field" /> or
    ///     <see cref="UiColumn{T}.SortBy" /> its provider can translate. Anything else is enumerated and
    ///     handled here. Both are synchronous; <see cref="Source" /> is the awaited one.
    /// </remarks>
    public IEnumerable<T>? Data { get; set; }

    /// <summary>Fetches one page, awaited.</summary>
    /// <remarks>
    ///     Called with the sort and page the grid wants, and again whenever either changes. It takes
    ///     precedence over <see cref="Data" />, which a grid in this mode does not need to set at all.
    ///     The rows it returns are rendered as they arrive — already ordered, already sliced — so the
    ///     grid does no sorting or paging of its own, and <see cref="UiGridPage{T}.Total" /> is what the
    ///     pager counts in.
    /// </remarks>
    public Fn<UiGridRequest, Task<UiGridPage<T>>>? Source { get; set; }

    /// <summary>The accessible name of the table.</summary>
    /// <remarks>
    ///     Optional, unlike the label on a form control: a table is not a control, and a page that has
    ///     already headed the grid with a real heading would only repeat itself. Give it one wherever the
    ///     surrounding markup does not already say what these rows are.
    /// </remarks>
    public string? Label { get; set; }

    /// <summary>How many rows a page holds. Unset — or zero — is no paging at all.</summary>
    public int? PageSize { get; set; }

    /// <summary>The page shown, counting from zero. Setting it hands paging to the parent.</summary>
    public int? Page { get; set; }

    /// <summary>Called with the page the reader asked for.</summary>
    public Callback<int>? OnPageChange { get; set; }

    /// <summary>Makes each page in the pager a link, from its page number counted from zero.</summary>
    /// <remarks>
    ///     For a grid whose page lives in the URL — <c>?page=2</c> — so a page can be shared, bookmarked and
    ///     reached with the back button. The link does the navigating, so <see cref="OnPageChange" /> is not
    ///     called: the new page arrives as <see cref="Page" /> on the render that follows. Counted from zero
    ///     like <see cref="Page" />, whatever the URL itself counts from.
    /// </remarks>
    public Fn<int, RouteUrl>? PageHref { get; set; }


    /// <summary>
    ///     How many rows stand behind the ones given, when <see cref="Data" /> holds one already-sliced
    ///     page.
    /// </summary>
    /// <remarks>
    ///     This is the mode where the fetch is in the parent's hands and the grid is told what it has:
    ///     the rows are rendered as given, and the pager counts in this instead of in their length. A
    ///     grid whose parent pages for it but leaves this unset renders a pager that always claims to be
    ///     one page long.
    /// </remarks>
    public int? TotalCount { get; set; }

    /// <summary>The <see cref="UiColumn{T}.Field" /> token sorted by. Setting it hands sorting over.</summary>
    public string? Sort { get; set; }

    /// <summary>Which way round the controlled sort runs.</summary>
    public bool? SortDescending { get; set; }

    /// <summary>Called with the sort the reader asked for.</summary>
    public Callback<UiGridSort>? OnSortChange { get; set; }


    /// <summary>Shades alternate rows.</summary>
    public bool? Zebra { get; set; }

    /// <summary>Lights a row under the pointer. Unset is on wherever a row is clickable.</summary>
    public bool? Hover { get; set; }

    /// <summary>How tight the rows are.</summary>
    public UiSize? Size { get; set; }

    /// <summary>Keeps the header visible while the rows scroll under it.</summary>
    /// <remarks>Needs a <see cref="MaxHeight" /> to scroll within, or there is nothing to scroll past.</remarks>
    public bool? StickyHeader { get; set; }

    /// <summary>A CSS length the table scrolls within — <c>"24rem"</c>, <c>"60vh"</c>.</summary>
    public string? MaxHeight { get; set; }

    /// <summary>Marks the grid as refetching, dimming it and announcing it as busy.</summary>
    public bool? Loading { get; set; }

    /// <summary>What to show when there are no rows at all.</summary>
    public Component? Empty { get; set; }

    /// <summary>An expandable detail row under each row. Returning null gives that row no expander.</summary>
    public Fn<T, Component?>? Detail { get; set; }

    /// <summary>Extra classes for one row, from the row.</summary>
    public Fn<T, string?>? RowClass { get; set; }

    /// <summary>Tints one row with a tone, from the row — a dead letter in <see cref="UiTone.Error" />.</summary>
    /// <remarks>
    ///     A typed tone rather than a <see cref="RowClass" />, because a class written in a consuming library is
    ///     a class the kit's compiled sheet never saw. The tint here is a complete literal, so it is in the sheet.
    /// </remarks>
    public Fn<T, UiTone?>? RowTone { get; set; }

    /// <summary>Called with the row that was clicked.</summary>
    /// <remarks>
    ///     Only the cells of columns that are <see cref="UiColumn{T}.RowClickable" /> fire it, which by
    ///     default is every column that is not a custom cell — see that property for why.
    /// </remarks>
    public Callback<T>? OnRowClick { get; set; }


    /// <summary>
    ///     Draws a row as a card below <c>sm</c>, replacing the automatic one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Unset is not "no card layout".</b> Below <c>sm</c> the table restyles itself into
    ///         stacked, labelled lines — each cell keeps its column's title in front of it — and that
    ///         costs nothing, because it is the SAME markup under different utilities rather than a
    ///         second copy of every cell.
    ///     </para>
    ///     <para>
    ///         Setting this replaces those lines with markup of your own, and that is the version that
    ///         costs: the authored cards and the table both render, the table hidden below <c>sm</c> and
    ///         the cards above it. Reach for it when a phone wants genuinely different content, not
    ///         merely the same cells stacked.
    ///     </para>
    /// </remarks>
    public Fn<T, Component>? Card { get; set; }

    /// <summary>Markup above the table — filters, actions, a count.</summary>
    public Component? Toolbar { get; set; }

    /// <summary>Offers a menu that shows, hides and reorders the columns.</summary>
    public bool? ColumnChooser { get; set; }

    /// <summary>The hidden columns, by field token. Setting it hands that axis over.</summary>
    public IReadOnlyList<string>? HiddenColumns { get; set; }

    /// <summary>Called with the hidden columns after the reader changed them.</summary>
    public Callback<IReadOnlyList<string>>? OnHiddenColumnsChange { get; set; }


    /// <summary>The column order, by field token. Setting it hands that axis over.</summary>
    /// <remarks>Tokens it does not name keep their declared position, after the ones it does.</remarks>
    public IReadOnlyList<string>? ColumnOrder { get; set; }

    /// <summary>Called with the column order after the reader changed it.</summary>
    public Callback<IReadOnlyList<string>>? OnColumnOrderChange { get; set; }


    /// <summary>The columns grouped by, outermost first. Setting it hands that axis over.</summary>
    public IReadOnlyList<string>? Grouped { get; set; }

    /// <summary>Called with the grouping after the reader changed it.</summary>
    public Callback<IReadOnlyList<string>>? OnGroupedChange { get; set; }


    /// <summary>Shows the panel that groups, ungroups and reorders the grouping.</summary>
    public bool? GroupPanel { get; set; }

    /// <summary>Lets a band be folded shut. Unset is on.</summary>
    public bool? GroupCollapsible { get; set; }

    /// <summary>Repeats the column footers per band.</summary>
    public bool? GroupSubtotals { get; set; }

    /// <summary>
    ///     Keeps a grouped column in the table. Unset hides it.
    /// </summary>
    /// <remarks>
    ///     A grouped column holds the same value for every row of its band, and that value is already the
    ///     band's heading — so repeating it down the band is a column of one repeated word.
    /// </remarks>
    public bool? ShowGroupedColumns { get; set; }

    /// <summary>Extra classes for the grid's outermost element.</summary>
    public string? Class { get; set; }

    // The grid holds sort, page, expansion, grouping and column layout in FIELDS, none of which the
    // framework can see as props. Without this a click that changes only a field re-renders nothing —
    // the cache compares the props it was given, finds them identical, and keeps the previous frame.
    // Same reasoning as DragDrop and VirtualizeModel.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    /// <summary>
    ///     Describes the grid's columns, ending the chain:
    ///     <c>UiDataGrid.Rows(_rows)[c =&gt; [ c.Field(r =&gt; r.Name), c.Column()[ … ] ]]</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This lived on a chain type of its own (<c>GridBuild&lt;T, TKey&gt;</c>) for exactly one
    ///         reason: an indexer cannot be constrained, so offering it on a grid and nowhere else meant
    ///         giving the grid's chain a different TYPE. The chain receives on the component now, so the
    ///         indexer is declared on the only component it was ever meant for.
    ///     </para>
    ///     <para>
    ///         The lambda takes the GRID rather than a row, which is what gives <c>c.Field(…)</c> a type
    ///         to infer from — a bare <c>UiColumn.Field(…)</c> written as a flat child has nothing.
    ///     </para>
    ///     <para>
    ///         The factory is stored, not called: it runs on every render, inside the render walk, so a
    ///         column it builds keeps its identity across renders.
    ///     </para>
    /// </remarks>
    /// <param name="columns">Builds the columns, given the grid they belong to.</param>
    public Component this[Func<UiDataGrid<T, TKey>, IEnumerable<Component?>> columns]
    {
        get
        {
            _columnFactory = columns;
            return this;
        }
    }

    // ---- what is controlled, and what the grid is holding itself ---------------------------------

    // PageSize is nullable so that it is an OPTIONAL step. A non-nullable int with no initializer is a
    // REQUIRED chain step (RASK001), which would have made `UiDataGrid.Data(rows)` a pending state
    // rather than a finished chain — every later step then failing to infer its type arguments. Same
    // reason SortDescending is bool? rather than bool.
    private int Paging => PageSize ?? 0;

    private bool PageControlled => Page is not null;

    private bool SortControlled =>
        Sort is not null || OnSortChange is not null;

    private bool GroupControlled =>
        Grouped is not null || OnGroupedChange is not null;

    private bool HideControlled =>
        HiddenColumns is not null || OnHiddenColumnsChange is not null;

    private bool OrderControlled =>
        ColumnOrder is not null || OnColumnOrderChange is not null;

    private int CurrentPage => Page ?? _page;

    private string? CurrentSort => SortControlled ? Sort : _sortField;

    private bool CurrentSortDescending => SortControlled ? SortDescending is true : _sortDescending;

    private IReadOnlyList<string> CurrentGrouped => GroupControlled ? Grouped ?? [] : _grouped;

    private IReadOnlyList<string> CurrentHidden => HideControlled ? HiddenColumns ?? [] : _hidden;

    private IReadOnlyList<string> CurrentOrder => OrderControlled ? ColumnOrder ?? [] : _order;

    // Reordering is offered wherever the chooser is, or wherever the parent is driving the order.
    private bool ReorderEnabled => ColumnChooser is true || OrderControlled;

    private bool Expandable => Detail is not null;

    private bool SelectionEnabled => Selected is not null || OnSelectionChange is not null;

    private bool Busy => Loading is true;

    private int LeadingCells => (SelectionEnabled ? 1 : 0) + (Expandable ? 1 : 0);

    // ---- lifecycle: the awaited source ----------------------------------------------------------

    /// <inheritdoc />
    protected override Task OnMountAsync() => FetchAsync();

    /// <inheritdoc />
    protected override Task OnPropsChangedAsync() => FetchAsync();

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

    // ---- handlers -------------------------------------------------------------------------------

    // Whichever the caller supplied. Both set would be a call-site bug, and the async one wins because
    // it is the one that does work.
    // One handler, either shape. This used to take the sync and async halves of a pair and decide which
    // won; the carrier holds exactly one, and `Invoke` hands back null when it was the synchronous one —
    // so the completed task is supplied here rather than a state machine being created for it.
    private static Task Raise<TArg>(Callback<TArg>? handler, TArg arg) =>
        handler?.Invoke(arg) ?? Task.CompletedTask;

    // Ascending, descending, then off. The third state is not decoration: it is the only way back to the
    // order the source itself chose, which for a query is whatever the store returns and for a list is
    // the order the caller put them in.
    private async Task ToggleSortAsync(UiColumn<T> column)
    {
        var token = column.FieldName;
        if (token is null)
        {
            return;
        }

        var (field, descending) =
            !string.Equals(CurrentSort, token, StringComparison.Ordinal) ? (token, false)
            : !CurrentSortDescending ? (token, true)
            : ((string?)null, false);

        if (!SortControlled)
        {
            _sortField = field;
            _sortDescending = descending;
            // A new sort re-pages from the top: page four of the old order names different rows.
            _page = 0;
        }

        await Raise(OnSortChange, new UiGridSort(field, descending))
            .ConfigureAwait(false);
        await FetchAsync().ConfigureAwait(false);
    }

    private async Task GoToPageAsync(int page, int pageCount)
    {
        var target = Math.Clamp(page, 0, Math.Max(pageCount - 1, 0));
        if (target == CurrentPage)
        {
            return;
        }

        if (!PageControlled)
        {
            _page = target;
        }

        await Raise(OnPageChange, target).ConfigureAwait(false);
        await FetchAsync().ConfigureAwait(false);
    }

    private void ToggleExpand(object key)
    {
        if (!_expanded.Remove(key))
        {
            _expanded.Add(key);
        }
    }

    private void ToggleBand(string path)
    {
        if (!_collapsed.Remove(path))
        {
            _collapsed.Add(path);
        }
    }

    private Task SetGroupedAsync(IReadOnlyList<string> next)
    {
        if (!GroupControlled)
        {
            _grouped.Clear();
            _grouped.AddRange(next);
            // Bands are addressed by the path of keys above them, and regrouping renames every path.
            _collapsed.Clear();
            _page = 0;
        }

        return Raise(OnGroupedChange, next);
    }

    private Task SetHiddenAsync(IReadOnlyList<string> next)
    {
        if (!HideControlled)
        {
            _hidden.Clear();
            _hidden.AddRange(next);
        }

        return Raise(OnHiddenColumnsChange, next);
    }

    private Task SetOrderAsync(IReadOnlyList<string> next)
    {
        if (!OrderControlled)
        {
            _order.Clear();
            _order.AddRange(next);
        }

        return Raise(OnColumnOrderChange, next);
    }

    private Task ToggleHiddenAsync(string token)
    {
        var next = new List<string>(CurrentHidden);
        if (!next.Remove(token))
        {
            next.Add(token);
        }

        return SetHiddenAsync(next);
    }

    private Task GroupByAsync(string token)
    {
        if (CurrentGrouped.Contains(token, StringComparer.Ordinal))
        {
            return Task.CompletedTask;
        }

        return SetGroupedAsync([.. CurrentGrouped, token]);
    }

    private Task UngroupAsync(string token)
    {
        var next = new List<string>(CurrentGrouped);
        return next.Remove(token) ? SetGroupedAsync(next) : Task.CompletedTask;
    }

    private Task MoveAsync(IReadOnlyList<string> list, string token, int delta,
        Func<IReadOnlyList<string>, Task> commit)
    {
        var next = new List<string>(list);
        var from = next.IndexOf(token);
        if (from < 0)
        {
            return Task.CompletedTask;
        }

        var to = from + delta;
        if (to < 0 || to >= next.Count)
        {
            return Task.CompletedTask;
        }

        next.RemoveAt(from);
        next.Insert(to, token);
        return commit(next);
    }

    private Task MoveGroupAsync(string token, int delta) =>
        MoveAsync(CurrentGrouped, token, delta, SetGroupedAsync);

    // Reordering a column needs the FULL order to move within, not the sparse one the parent may have
    // given: a token nobody has named yet still has a position, and moving its neighbour past it has to
    // move it too.
    private Task MoveColumnAsync(IReadOnlyList<UiColumn<T>> columns, string token, int delta) =>
        MoveAsync(EffectiveOrder(columns), token, delta, SetOrderAsync);

    private List<string> EffectiveOrder(IReadOnlyList<UiColumn<T>> columns)
    {
        var order = new List<string>(CurrentOrder);
        foreach (var column in columns)
        {
            if (column.FieldName is { } token && !order.Contains(token, StringComparer.Ordinal))
            {
                order.Add(token);
            }
        }

        return order;
    }

    // ---- columns --------------------------------------------------------------------------------

    /// <summary>
    ///     Opens a column bound to a member of the row: its identity, its header and its cell value.
    /// </summary>
    /// <remarks>
    ///     Reached as <c>c.Field(p =&gt; p.Name)</c> inside the grid's column factory. An instance method
    ///     rather than an entry, and that is the whole point: the grid's own type argument is already
    ///     fixed here, so <c>p</c> has a type — where <c>UiColumn.Field(p =&gt; p.Name)</c> written as a
    ///     flat child would have nothing to infer one from.
    /// </remarks>
    /// <param name="field">The member this column is about.</param>
    public UiColumn<T> Field(Expression<Func<T, object?>> field) => UiColumn.Field(field);

    /// <summary>
    ///     Opens a column bound to no member — an actions column, or one computed from the whole row.
    /// </summary>
    /// <remarks>
    ///     It has no field token, so it can be shown but never sorted, grouped, hidden or reordered by
    ///     name. Give it a <see cref="UiColumn{T}.Cell" /> and a <see cref="UiColumn{T}.Title" />.
    /// </remarks>
    public UiColumn<T> Column() => UiColumn.Of<T>();

    // The columns, built once per render. ONCE is load-bearing: each `c.Field(…)` takes the next entry
    // slot under this grid, so calling the factory a second time in one render would hand every column a
    // different instance from the one the first pass built — and grow the slot list every frame.
    private List<UiColumn<T>> ResolveColumns()
    {
        if (_columnFactory is not Func<UiDataGrid<T, TKey>, IEnumerable<Component?>> factory)
        {
            return [];
        }

        var columns = new List<UiColumn<T>>();
        foreach (var child in factory(this))
        {
            // Anything that is not a column is ignored rather than rejected, which is what lets one arm
            // of a conditional be null — `admin ? c.Field(…) : null`.
            if (child is UiColumn<T> column)
            {
                columns.Add(column);
            }
        }

        return columns;
    }

    private bool IsHidden(UiColumn<T> column) =>
        column.FieldName is { } token && CurrentHidden.Contains(token, StringComparer.Ordinal);

    // A grouped column is drawn as band headings rather than as a column of one repeated value, so it
    // leaves the table unless the caller asks for it back.
    private bool IsGroupedAway(UiColumn<T> column) =>
        ShowGroupedColumns is not true
        && column.FieldName is { } token
        && CurrentGrouped.Contains(token, StringComparer.Ordinal);

    private List<UiColumn<T>> VisibleColumns(IReadOnlyList<UiColumn<T>> columns)
    {
        var visible = new List<UiColumn<T>>(columns.Count);
        foreach (var column in Ordered(columns))
        {
            if (!IsHidden(column) && !IsGroupedAway(column))
            {
                visible.Add(column);
            }
        }

        return visible;
    }

    // Columns the order names, in the order it names them; everything else after, in declared order. A
    // token the order does not mention is not an error — a chooser that has moved one column has said
    // nothing about the rest.
    private List<UiColumn<T>> Ordered(IReadOnlyList<UiColumn<T>> columns)
    {
        var order = CurrentOrder;
        if (order.Count == 0)
        {
            return [.. columns];
        }

        var ranked = new List<UiColumn<T>>(columns.Count);
        foreach (var token in order)
        {
            foreach (var column in columns)
            {
                if (string.Equals(column.FieldName, token, StringComparison.Ordinal)
                    && !ranked.Contains(column))
                {
                    ranked.Add(column);
                }
            }
        }

        foreach (var column in columns)
        {
            if (!ranked.Contains(column))
            {
                ranked.Add(column);
            }
        }

        return ranked;
    }

    private List<UiColumn<T>> GroupColumns(IReadOnlyList<UiColumn<T>> columns)
    {
        var grouped = new List<UiColumn<T>>();
        foreach (var token in CurrentGrouped)
        {
            foreach (var column in columns)
            {
                if (string.Equals(column.FieldName, token, StringComparison.Ordinal))
                {
                    grouped.Add(column);
                    break;
                }
            }
        }

        return grouped;
    }

    // ---- rows -----------------------------------------------------------------------------------

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
            ordered = ordered is null
                ? CurrentSortDescending ? query.OrderByDescending(key) : query.OrderBy(key)
                : CurrentSortDescending ? ordered.ThenByDescending(key) : ordered.ThenBy(key);
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
        if (CurrentSort is not { } token)
        {
            return null;
        }

        foreach (var column in columns)
        {
            if (string.Equals(column.FieldName, token, StringComparison.Ordinal))
            {
                return column;
            }
        }

        return null;
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
        foreach (var group in groups)
        {
            ordered = ordered is null
                ? rows.OrderBy(group.BandOrder)
                : ordered.ThenBy(group.BandOrder);
        }

        if (sorted is not null)
        {
            ordered = ordered is null
                ? CurrentSortDescending
                    ? rows.OrderByDescending(sorted.SortOf)
                    : rows.OrderBy(sorted.SortOf)
                : CurrentSortDescending
                    ? ordered.ThenByDescending(sorted.SortOf)
                    : ordered.ThenBy(sorted.SortOf);
        }

        return ordered is null ? rows : [.. ordered];
    }

    private IReadOnlyList<T> Slice(IReadOnlyList<T> rows)
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

        var end = Math.Min(start + Paging, rows.Count);
        var page = new List<T>(end - start);
        for (var i = start; i < end; i++)
        {
            page.Add(rows[i]);
        }

        return page;
    }

    private int PageCount(int total) =>
        Paging <= 0 ? 1 : Math.Max(1, (total + Paging - 1) / Paging);

    // ---- selection ------------------------------------------------------------------------------

    private bool IsSelected(T row) =>
        Selected is { } controlled ? controlled.Contains(RowKey(row)) : _own.Contains(RowKey(row));

    private bool AllSelected(IReadOnlyList<T> rows)
    {
        if (rows.Count == 0)
        {
            return false;
        }

        foreach (var row in rows)
        {
            if (!IsSelected(row))
            {
                return false;
            }
        }

        return true;
    }

    private Task ToggleAsync(T row, bool on)
    {
        var next = CurrentSelection();
        if (on)
        {
            next.Add(RowKey(row));
        }
        else
        {
            next.Remove(RowKey(row));
        }

        return CommitSelectionAsync(next);
    }

    private Task SetPageSelectionAsync(IReadOnlyList<T> rows, bool on)
    {
        var next = CurrentSelection();
        foreach (var row in rows)
        {
            if (on)
            {
                next.Add(RowKey(row));
            }
            else
            {
                next.Remove(RowKey(row));
            }
        }

        return CommitSelectionAsync(next);
    }

    private HashSet<TKey> CurrentSelection() => Selected is { } c ? [.. c] : [.. _own];

    private Task CommitSelectionAsync(HashSet<TKey> next)
    {
        // The uncontrolled half is only ours to hold while the parent is not holding it.
        if (Selected is null)
        {
            _own.Clear();
            foreach (var key in next)
            {
                _own.Add(key);
            }
        }

        return OnSelectionChange?.Invoke(next.ToList()) ?? Task.CompletedTask;
    }

    // A row's identity for the live diff. It is the row KEY now, never the index: an index makes two
    // rows that swapped places look like two rows that changed contents, which is the reordering bug
    // the key was always meant to prevent — and before RowKey was required, an index was the fallback
    // every grid that had not named one silently got.
    private object RowIdentity(T row) => RowKey(row);

    // ---- render ---------------------------------------------------------------------------------

    // The automatic card layout is the SAME cells under different utilities, so it costs nothing but the
    // classes. It is off wherever the caller drew their own card, because then there are two layouts and
    // the table is simply hidden below sm.
    //
    // EVERY responsive class here is a `max-sm:` VARIANT, never a base utility plus an `sm:` override,
    // and that is a cross-stylesheet rule rather than a preference. The kit ships its own compiled sheet
    // and the consuming app links its own after it; CSS layers do not merge across <link> sheets, so an
    // app that writes `hidden` anywhere emits an unconditional `.hidden{display:none}` that lands LATER
    // in the cascade than this sheet's `sm:table-header-group` and wins at every width. Written the
    // other way round there is no base class to overrule: above `sm` the element keeps the display a
    // table gives it, and only the variant — a name the app's sheet has no reason to hold a different
    // opinion about — applies below.
    private bool StackedCards => Card is null;

    private string CellClass(UiColumn<T> column) =>
        UiClass.Compose(
            // overflow-wrap:anywhere, because a cell holding one unbroken token — a type name, a request id, a
            // path — otherwise sets the table's minimum width, and the table spills out of a phone or scrolls
            // sideways on a desk. "anywhere" rather than "break-word" is the half that matters: only it lets the
            // token break while the column widths are being worked out.
            "wrap-anywhere",
            StackedCards
                ? "max-sm:flex max-sm:items-baseline max-sm:justify-between max-sm:gap-3 "
                  + "max-sm:before:font-medium max-sm:before:text-base-content/60 "
                  + "max-sm:before:content-[attr(data-label)]"
                : "",
            column.CellClasses);

    // Shared and immutable, so the common case — a busy-free, unnamed grid — allocates nothing for its
    // aria bag. Only a grid that is both named and busy builds one.
    private static readonly IReadOnlyDictionary<string, string?> AriaBusy =
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["busy"] = "true" };

    private IReadOnlyDictionary<string, string?>? TableAria()
    {
        if (Label is not { } label)
        {
            return Busy ? AriaBusy : null;
        }

        var aria = new Dictionary<string, string?>(StringComparer.Ordinal) { ["label"] = label };
        if (Busy)
        {
            aria["busy"] = "true";
        }

        return aria;
    }

    /// <inheritdoc />
    protected override Component? Render()
    {
        // ONCE per render — see ResolveColumns. Everything below reads this list.
        var columns = ResolveColumns();
        var visible = VisibleColumns(columns);
        var groups = GroupColumns(columns);
        var rows = ResolveRows(columns);
        var span = visible.Count + LeadingCells;

        var table = Table
            .Class(UiClass.Compose(
                "table w-full",
                Zebra is true ? "table-zebra" : "",
                Size is { } size ? UiClassNames.TableSize(size) : "",
                StickyHeader is true ? "table-pin-rows" : ""))
            // One Aria call, not two: a second would replace the first outright rather than add to it,
            // which is what RASK044 reports. aria-busy goes on the TABLE rather than on a wrapper around
            // the spinner, because a live region inside an aria-busy subtree has its announcement
            // deferred until busy clears — by which point the load is over and was never announced.
            .Aria(TableAria())[
            Head(visible, rows.Rows),
            Tbody[Body(visible, groups, rows, span)],
            Foot(visible, rows.All, span)
        ];

        var scroller = Div
            .Class(UiClass.Compose(
                "overflow-x-auto rounded-xl border border-base-300 bg-base-100",
                MaxHeight is not null ? "overflow-y-auto" : "",
                Busy ? "opacity-60" : "",
                Card is not null ? "max-sm:hidden" : ""))
            .Style(MaxHeight is { } max ? "max-height:" + max : null)[table];

        return Div.Class(UiClass.Compose("flex flex-col gap-3", Class))[
            ToolbarRow(),
            Chrome(columns, groups),
            scroller,
            Cards(rows.Rows),
            Pager(rows)
        ];
    }

    // ---- header ---------------------------------------------------------------------------------

    private Component Head(IReadOnlyList<UiColumn<T>> visible, IReadOnlyList<T> pageRows) =>
        Thead.Class(StackedCards ? "max-sm:hidden" : null)[
            Tr[
                SelectionEnabled ? Th.Class("w-0")[SelectAllBox(pageRows)] : null,
                Expandable ? Th.Class("w-0").Aria("label", "Expand") : null,
                visible.Select(HeaderCell)
            ]
        ];

    // Shared and immutable, so a header row allocates nothing to say how it is sorted. Deliberately
    // non-generic-free of per-column state: there are only ever three answers.
    private static readonly IReadOnlyDictionary<string, string?> SortNone =
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["sort"] = "none" };

    private static readonly IReadOnlyDictionary<string, string?> SortAscending =
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["sort"] = "ascending" };

    private static readonly IReadOnlyDictionary<string, string?> SortDescendingAria =
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["sort"] = "descending" };

    private IReadOnlyDictionary<string, string?> SortAria(bool sorted) =>
        !sorted ? SortNone : CurrentSortDescending ? SortDescendingAria : SortAscending;

    private Component HeaderCell(UiColumn<T> column)
    {
        var sorted = column.FieldName is { } token
            && string.Equals(CurrentSort, token, StringComparison.Ordinal);

        // A column can only be sorted by name, so one with no field token gets a plain header rather
        // than a control that would do nothing.
        var sortable = column.Sortable is true && column.FieldName is not null;
        var groupable = column.Groupable is true && column.FieldName is not null;

        var head = Th
            .Key(column.FieldName ?? column.Title ?? "")
            .Scope("col")
            .Class(column.HeaderClasses);

        // Only where there is a sort state to report. Passing null writes a BARE `aria-sort`, which is
        // not "no sort state" — it is an aria-sort with no value, on a header that cannot be sorted.
        if (sortable)
        {
            head = head.Aria(SortAria(sorted));
        }

        return head[
            Div.Class("flex items-center gap-1")[
                sortable
                    ? Button
                        .Type("button")
                        .Class("inline-flex items-center gap-1 font-medium hover:underline")
                        .Disabled(Busy)
                        .OnClick(() => ToggleSortAsync(column))[
                        column.Title ?? "",
                        UiIcon
                            .Name(!sorted ? UiIconName.ArrowsUpDown
                                : CurrentSortDescending ? UiIconName.ChevronDown : UiIconName.ChevronUp)
                            .Class("size-3 shrink-0 opacity-60")
                    ]
                    : (Component)Span[column.Title ?? ""],
                groupable ? GroupToggle(column) : null
            ]
        ];
    }

    private Component GroupToggle(UiColumn<T> column)
    {
        var token = column.FieldName!;
        var on = CurrentGrouped.Contains(token, StringComparer.Ordinal);
        return UiButton
            .AccessibleLabel(on ? "Stop grouping by " + (column.Title ?? token) : "Group by " + (column.Title ?? token))
            .Square(true)
            .Size(UiSize.Xs)
            .Variant(on ? UiVariant.Soft : UiVariant.Ghost)
            .Disabled(Busy)
            .OnClick(() => on ? UngroupAsync(token) : GroupByAsync(token))[UiIcon.Name(UiIconName.Stack)];
    }

    private Component SelectAllBox(IReadOnlyList<T> pageRows)
    {
        // "Select all" would be a lie wherever a pager is: the grid holds one page and can only name the
        // keys it has.
        //
        // Of<bool>() rather than a value: the type argument is what makes the input a checkbox and
        // OnChange a bool, the same way UiCheckbox opens.
        return Input
            .Of<bool>()
            .Checked(AllSelected(pageRows))
            .OnChange(on => SetPageSelectionAsync(pageRows, on))
            .Class("checkbox checkbox-sm")
            .Aria("label", "Select all rows on this page")
            .Disabled(Busy);
    }

    // ---- body -----------------------------------------------------------------------------------

    private IEnumerable<Component?> Body(
        IReadOnlyList<UiColumn<T>> visible, IReadOnlyList<UiColumn<T>> groups, Resolved rows, int span)
    {
        if (rows.Rows.Count == 0)
        {
            yield return Tr[
                Td.Colspan(span).Class("py-10 text-center text-base-content/60")[
                    Empty ?? (Component)"Nothing to show."
                ]
            ];
            yield break;
        }

        if (groups.Count == 0)
        {
            foreach (var component in Rows(visible, rows.Rows, span, 0))
            {
                yield return component;
            }

            yield break;
        }

        foreach (var component in Bands(visible, groups, rows.Rows, span))
        {
            yield return component;
        }
    }

    private IEnumerable<Component?> Rows(
        IReadOnlyList<UiColumn<T>> visible, IReadOnlyList<T> rows, int span, int offset)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var key = RowIdentity(row);
            var open = Expandable && _expanded.Contains(key);

            yield return Tr
                .Key(key)
                .Class(UiClass.Compose(
                    StackedCards ? "max-sm:block max-sm:border-b max-sm:border-base-300" : "",
                    Hover ?? OnRowClick is not null
                        ? "hover:bg-base-200"
                        : "",
                    RowTone?.Invoke(row) is { } tone ? UiClassNames.RowTone(tone) : "",
                    RowClass?.Invoke(row)))[
                SelectionEnabled ? Td.Class("w-0")[SelectBox(row)] : null,
                Expandable ? Td.Class("w-0")[Expander(row, key, open)] : null,
                visible.Select(column => Cell(column, row))
            ];

            if (open && Detail?.Invoke(row) is { } detail)
            {
                yield return Tr.Key(key.ToString() + "-detail")[
                    Td.Colspan(span).Class("bg-base-200/50")[detail]
                ];
            }
        }
    }

    private Component Cell(UiColumn<T> column, T row)
    {
        var cell = Td
            .Key(column.FieldName ?? column.Title ?? "")
            .Class(CellClass(column));

        // Only where the stacked layout will read it. Passing null writes a BARE `data-label`, so a grid
        // with its own card markup carried an empty attribute on every cell it had.
        if (StackedCards && column.Title is { } title)
        {
            cell = cell.Data("label", title);
        }

        // The row-click handler goes on the CELLS rather than the row, so a column can carve itself out
        // of it — see UiColumn.RowClickable for why a custom cell does so by default.
        if (column.IsRowClickable && RowClickHandler(row) is { } click)
        {
            cell = cell.OnClick(click).Class(UiClass.Compose(CellClass(column), "cursor-pointer"));
        }

        return cell[column.Body(row)];
    }

    // One handler either way. `Invoke` returns null for a synchronous one, so the completed task is
    // supplied here rather than a state machine being created for it.
    private Func<Task>? RowClickHandler(T row) =>
        OnRowClick is { } click ? () => click.Invoke(row) ?? Task.CompletedTask : null;

    private Component SelectBox(T row)
    {
        return Input
            .Of<bool>()
            .Checked(IsSelected(row))
            .OnChange(on => ToggleAsync(row, on))
            .Class("checkbox checkbox-sm")
            .Aria("label", "Select row")
            .Disabled(Busy);
    }

    private Component Expander(T row, object key, bool open) =>
        Detail?.Invoke(row) is null
            ? Span
            : UiButton
                .AccessibleLabel(open ? "Collapse row" : "Expand row")
                .Square(true)
                .Size(UiSize.Xs)
                .Variant(UiVariant.Ghost)
                .OnClick(() => ToggleExpand(key))[UiIcon.Name(open ? UiIconName.ChevronDown : UiIconName.ChevronRight)];

    // ---- bands ----------------------------------------------------------------------------------

    // Bands, drawn by recursion over the grouping levels.
    //
    // Recursive rather than a single pass with a running "where did this band start" index, and that is
    // a correctness point rather than a stylistic one. With two levels the OUTER subtotal has to span
    // every inner band beneath it while the inner one spans only its own run, and one start index cannot
    // say both — it reports the outer total as the last inner band's. Recursing on the parent band's own
    // slice also means an inner run is compared only against its siblings, so two bands that happen to
    // share an inner key under different parents stay apart instead of merging into one.
    private IEnumerable<Component?> Bands(
        IReadOnlyList<UiColumn<T>> visible, IReadOnlyList<UiColumn<T>> groups, IReadOnlyList<T> rows,
        int span) =>
        Band(visible, groups, rows, span, 0, 0, []);

    private IEnumerable<Component?> Band(
        IReadOnlyList<UiColumn<T>> visible, IReadOnlyList<UiColumn<T>> groups, IReadOnlyList<T> rows,
        int span, int level, int offset, IReadOnlyList<object?> above)
    {
        // Past the last grouped column: what is left is ordinary rows.
        if (level == groups.Count)
        {
            foreach (var component in Rows(visible, rows, span, offset))
            {
                yield return component;
            }

            yield break;
        }

        var column = groups[level];
        var at = 0;

        while (at < rows.Count)
        {
            // The rows arrive already ordered by every group key (see SortInMemory), so a band is a run
            // of adjacent rows sharing this level's key — no dictionary, and the run keeps the order the
            // reader sees.
            var key = column.Band(rows[at]);
            var end = at;
            while (end < rows.Count && Equals(column.Band(rows[end]), key))
            {
                end++;
            }

            var band = Slice(rows, at, end);
            var keys = new List<object?>(above) { key };
            var path = Path(keys, keys.Count);

            yield return BandHeader(column, key, band, path, level, span);

            if (!(GroupCollapsible is not false && _collapsed.Contains(path)))
            {
                foreach (var component in Band(visible, groups, band, span, level + 1, offset + at, keys))
                {
                    yield return component;
                }

                if (GroupSubtotals is true)
                {
                    yield return Subtotal(visible, band, level, span);
                }
            }

            at = end;
        }
    }

    // A band is addressed by the keys above it, joined — so a collapsed "Europe / France" survives a
    // re-render and does not collapse "Asia / France" with it. A unit separator rather than a printable
    // one, so a key that happens to contain the separator cannot forge another band's path.
    private static string Path(IReadOnlyList<object?> keys, int depth)
    {
        var parts = new string[depth];
        for (var i = 0; i < depth; i++)
        {
            parts[i] = keys[i]?.ToString() ?? "";
        }

        return string.Join('\u001f', parts);
    }

    private static List<T> Slice(IReadOnlyList<T> rows, int from, int to)
    {
        var slice = new List<T>(Math.Max(to - from, 0));
        for (var i = from; i < to && i < rows.Count; i++)
        {
            slice.Add(rows[i]);
        }

        return slice;
    }

    private Component BandHeader(
        UiColumn<T> column, object? key, IReadOnlyList<T> band, string path, int level, int span)
    {
        var collapsed = _collapsed.Contains(path);
        var heading = column.GroupHeader is { } custom && custom.Invoke(key, band) is { } drawn
            ? drawn
            : (Component)Span.Class("font-medium")[
                (column.Title ?? "") + ": " + (key?.ToString() ?? "—")
                + " (" + band.Count.ToString(CultureInfo.InvariantCulture) + ")"
            ];

        return Tr.Key("band-" + path)[
            Td.Colspan(span).Class("bg-base-200")[
                Div.Class("flex items-center gap-2").Style("padding-inline-start:" + level + "rem")[
                    GroupCollapsible is false
                        ? null
                        : UiButton
                            .AccessibleLabel(collapsed ? "Expand group" : "Collapse group")
                            .Square(true)
                            .Size(UiSize.Xs)
                            .Variant(UiVariant.Ghost)
                            .OnClick(() => ToggleBand(path))[UiIcon.Name(collapsed ? UiIconName.ChevronRight : UiIconName.ChevronDown)],
                    heading
                ]
            ]
        ];
    }

    private Component Subtotal(
        IReadOnlyList<UiColumn<T>> visible, IReadOnlyList<T> band, int level, int span) =>
        Tr.Key("subtotal-" + level + "-" + band.Count)[
            LeadingCells > 0 ? Td.Colspan(LeadingCells).Class("bg-base-100") : null,
            visible.Select(column =>
                Td.Key(column.FieldName ?? column.Title ?? "")
                    .Class(UiClass.Compose("bg-base-100 font-medium", column.Class))[
                    column.HasFooter ? column.Foot(band) : (Component)""
                ])
        ];

    // ---- the chrome above the table -------------------------------------------------------------

    // The column chooser and the group panel, wrapped in the framework's headless drag-and-drop so both
    // can also be reordered by dragging.
    //
    // BUTTONS ARE THE MECHANISM AND DRAG IS THE BONUS, which is a decision about who can use it rather
    // than about taste: HTML5 drag events do not fire on touch at all and cannot be driven from a
    // keyboard, so a panel whose only gesture was dragging would be unreachable on the phone this kit
    // designs for first. Both paths call the same handler, so there is one behaviour to test.
    private Component? Chrome(IReadOnlyList<UiColumn<T>> columns, IReadOnlyList<UiColumn<T>> groups)
    {
        var chooser = ColumnChooser is true;
        var panel = GroupPanel is true;
        if (!chooser && !panel)
        {
            return null;
        }

        return DragDrop
            .Body(ctx => Div.Class("flex flex-wrap items-start gap-2")[
                chooser ? ChooserBar(columns, ctx) : null,
                panel ? Panel(columns, groups, ctx) : null
            ])
            .OnDrop(move => DropAsync(move, columns));
    }

    // One drop, routed by the zone it landed in. The index is the position within that zone's own list,
    // which for the chooser is the effective column order and for the panel is the grouping.
    private Task DropAsync(DragDropMove move, IReadOnlyList<UiColumn<T>> columns)
    {
        if (!string.Equals(move.FromZone, move.ToZone, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        if (string.Equals(move.ToZone, ColumnZone, StringComparison.Ordinal))
        {
            var order = EffectiveOrder(columns);
            move.ApplyTo(order);
            return SetOrderAsync(order);
        }

        var grouped = new List<string>(CurrentGrouped);
        move.ApplyTo(grouped);
        return SetGroupedAsync(grouped);
    }

    private const string ColumnZone = "columns";
    private const string GroupZone = "groups";

    private Component ChooserBar(IReadOnlyList<UiColumn<T>> columns, DragDropContext ctx) =>
        Div.Class("relative")[
            UiButton
                .Size(UiSize.Sm)
                .Variant(UiVariant.Outline)
                .OnClick(() => _chooserOpen = !_chooserOpen)[UiIcon.Name(UiIconName.Menu), "Columns"],
            !_chooserOpen
                ? null
                : Div
                    .Class("absolute z-10 mt-1 flex w-64 flex-col gap-1 rounded-box border "
                        + "border-base-300 bg-base-100 p-2 shadow-lg")
                    .Aria("label", "Columns")
                    .Role("group")[ChooserRows(columns, ctx)]
        ];

    private IEnumerable<Component?> ChooserRows(IReadOnlyList<UiColumn<T>> columns, DragDropContext ctx)
    {
        var order = EffectiveOrder(columns);
        for (var i = 0; i < order.Count; i++)
        {
            var token = order[i];
            var column = Find(columns, token);

            // A column with no field token has no name to address it by, so it can be neither hidden nor
            // moved — it simply is not listed.
            if (column is null)
            {
                continue;
            }

            var index = i;
            var hidden = CurrentHidden.Contains(token, StringComparer.Ordinal);

            yield return Div
                .Key(token)
                .Class(UiClass.Compose(
                    "flex items-center gap-1 rounded px-1",
                    ctx.IsDropTarget(ColumnZone, index) ? "bg-base-200" : ""))
                .Draggable(column.CanReorder && ReorderEnabled)
                .OnDragStart(ctx.DragStart(ColumnZone, index))
                .OnDragOver(ctx.DragOver(ColumnZone, index))
                .OnDrop(ctx.Drop(ColumnZone, index))
                .OnDragEnd(ctx.DragEnd)[
                column.CanReorder && ReorderEnabled
                    ? UiIcon.Name(UiIconName.Grip).Class("size-3 shrink-0 opacity-40")
                    : null,
                // RaskMarkup.Label, not Label: this grid has a Label PROPERTY, and a component's own
                // member hides the injected entry of the same name — the rule that gives every kit
                // component its Ui prefix. Naming the base the entry lives on reaches the tag again.
                RaskMarkup.Label.Class("flex flex-1 items-center gap-2 text-sm")[
                    Input
                        .Of<bool>()
                        .Checked(!hidden)
                        .OnChange(_ => ToggleHiddenAsync(token))
                        .Class("checkbox checkbox-xs")
                        .Disabled(!column.CanHide),
                    column.Title ?? token
                ],
                MoveButton(column.CanReorder && ReorderEnabled && index > 0, "Move up",
                    UiIconName.ChevronUp, () => MoveColumnAsync(columns, token, -1)),
                MoveButton(column.CanReorder && ReorderEnabled && index < order.Count - 1, "Move down",
                    UiIconName.ChevronDown, () => MoveColumnAsync(columns, token, 1))
            ];
        }
    }

    private Component Panel(
        IReadOnlyList<UiColumn<T>> columns, IReadOnlyList<UiColumn<T>> groups, DragDropContext ctx) =>
        Div
            .Class("flex flex-wrap items-center gap-2 rounded-box border border-dashed "
                + "border-base-300 p-2")
            .Aria("label", "Grouping")
            .Role("group")[
            // The chips as a sequence rather than wrapped in a Fragment: Fragment is internal to
            // Rask.Core, so its entry is private protected and no other assembly can name it. The
            // enumerable indexer takes the sequence directly, which is what Fragment would have done.
            groups.Count == 0
                ? [Span.Class("text-sm text-base-content/60")[
                    "Group by a column with its header button."
                ]]
                : GroupChips(columns, groups, ctx)
        ];

    private IEnumerable<Component?> GroupChips(
        IReadOnlyList<UiColumn<T>> columns, IReadOnlyList<UiColumn<T>> groups, DragDropContext ctx)
    {
        for (var i = 0; i < groups.Count; i++)
        {
            var column = groups[i];
            var token = column.FieldName!;
            var index = i;

            yield return Div
                .Key(token)
                .Class(UiClass.Compose(
                    "flex items-center gap-1 rounded-full bg-base-200 py-1 pe-1 ps-2 text-sm",
                    ctx.IsDropTarget(GroupZone, index) ? "ring-2 ring-primary" : ""))
                .Draggable(true)
                .OnDragStart(ctx.DragStart(GroupZone, index))
                .OnDragOver(ctx.DragOver(GroupZone, index))
                .OnDrop(ctx.Drop(GroupZone, index))
                .OnDragEnd(ctx.DragEnd)[
                UiIcon.Name(UiIconName.Grip).Class("size-3 shrink-0 opacity-40"),
                Span[column.Title ?? token],
                MoveButton(index > 0, "Move group left", UiIconName.ArrowLeft,
                    () => MoveGroupAsync(token, -1)),
                MoveButton(index < groups.Count - 1, "Move group right", UiIconName.ArrowRight,
                    () => MoveGroupAsync(token, 1)),
                MoveButton(true, "Stop grouping by " + (column.Title ?? token), UiIconName.Close,
                    () => UngroupAsync(token))
            ];
        }
    }

    private static Component? MoveButton(bool enabled, string label, UiIconName icon, Func<Task> click) =>
        UiButton
            .AccessibleLabel(label)
            .Square(true)
            .Size(UiSize.Xs)
            .Variant(UiVariant.Ghost)
            .Disabled(!enabled)
            .OnClick(click)[UiIcon.Name(icon)];

    private static UiColumn<T>? Find(IReadOnlyList<UiColumn<T>> columns, string token)
    {
        foreach (var column in columns)
        {
            if (string.Equals(column.FieldName, token, StringComparison.Ordinal))
            {
                return column;
            }
        }

        return null;
    }

    // ---- footer, cards, pager -------------------------------------------------------------------

    private Component? Foot(IReadOnlyList<UiColumn<T>> visible, IReadOnlyList<T> all, int span)
    {
        if (!visible.Any(static c => c.HasFooter))
        {
            return null;
        }

        return Tfoot.Class(StackedCards ? "max-sm:hidden" : null)[
            Tr[
                LeadingCells > 0 ? Td.Colspan(LeadingCells) : null,
                visible.Select(column =>
                    Td.Key(column.FieldName ?? column.Title ?? "").Class(column.CellClasses)[column.Foot(all)])
            ]
        ];
    }

    private Component? Cards(IReadOnlyList<T> rows) =>
        Card is not { } card
            ? null
            : Div.Class("max-sm:flex max-sm:flex-col max-sm:gap-2 sm:hidden")[
                rows.Select((row, i) =>
                    Div.Key(RowIdentity(row))
                        .Class("rounded-xl border border-base-300 bg-base-100 p-3")[card.Invoke(row)])
            ];

    // One row of controls from sm up, one control per line below it: three filters side by side at 360px
    // leave each too narrow to show the value it is set to, which is the one thing a filter has to show.
    // Stacked through max-sm: variants, for the same cross-sheet reason as the cells above.
    private Component? ToolbarRow() =>
        Toolbar is null
            ? null
            : Div.Class("flex flex-wrap items-center gap-2 max-sm:flex-col max-sm:items-stretch")[Toolbar];

    private Component? Pager(Resolved rows)
    {
        if (Paging <= 0 || rows.Pages <= 1)
        {
            return null;
        }

        var current = Math.Clamp(CurrentPage, 0, rows.Pages - 1) + 1;

        // The pager counts from one and the grid from zero; the conversion happens here, once, in both modes.
        return Div.Class("flex flex-wrap items-center justify-between gap-2")[
            Span.Class("text-sm text-base-content/60")[
                rows.Total.ToString(CultureInfo.InvariantCulture) + " rows"
            ],
            PageHref is { } href
                ? UiPagination
                    .Pages(rows.Pages)
                    .Current(current)
                    .Href(page => href.Invoke(page - 1))
                : UiPagination
                    .Pages(rows.Pages)
                    .Current(current)
                    .OnSelect(page => _ = GoToPageAsync(page - 1, rows.Pages))
        ];
    }
}

