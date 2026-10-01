using System.Linq.Expressions;
using Rask.Core.Routing;

namespace Rask;

/// <summary>
/// A table over a typed row sequence: sortable headers, paging, selection, expandable detail rows,
/// grouping, a column chooser, and a card layout on a phone.
/// </summary>
/// <remarks>
/// <para>
/// <b>Columns are the chain's children, and they arrive through a factory:</b>
/// <c>Ui.DataGrid.Data(_products)[c =&gt; [ c.Field(p =&gt; p.Name).Title("Product").Sortable(true) ]]</c>.
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
    // Uncontrolled state. Each of these is consulted only while the matching controlled property is
    // unset, which is what lets one axis be driven from outside while the rest keep holding their own.
    private readonly HashSet<object> _expanded = [];
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
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
    ///         instance of the GRID is being built. This one
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
    public Callback<IReadOnlyList<TKey>> OnSelectionChange { get; set; }

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
    public Callback<int> OnPageChange { get; set; }

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
    public Callback<UiGridSort> OnSortChange { get; set; }


    /// <summary>Shades alternate rows.</summary>
    public bool? Zebra { get; set; }

    /// <summary>Lights a row under the pointer. Unset is on wherever a row is clickable.</summary>
    public bool? Hover { get; set; }

    /// <summary>How tight the rows are.</summary>
    public Ui.Size? Size { get; set; }

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

    /// <summary>Tints one row with a tone, from the row — a dead letter in <see cref="Ui.Tone.Error" />.</summary>
    /// <remarks>
    ///     A typed tone rather than a <see cref="RowClass" />, because a class written in a consuming library is
    ///     a class the kit's compiled sheet never saw. The tint here is a complete literal, so it is in the sheet.
    /// </remarks>
    public Fn<T, Ui.Tone?>? RowTone { get; set; }

    /// <summary>Called with the row that was clicked.</summary>
    /// <remarks>
    ///     Only the cells of columns that are <see cref="UiColumn{T}.RowClickable" /> fire it, which by
    ///     default is every column that is not a custom cell — see that property for why.
    /// </remarks>
    public Callback<T> OnRowClick { get; set; }


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
    public Callback<IReadOnlyList<string>> OnHiddenColumnsChange { get; set; }


    /// <summary>The column order, by field token. Setting it hands that axis over.</summary>
    /// <remarks>Tokens it does not name keep their declared position, after the ones it does.</remarks>
    public IReadOnlyList<string>? ColumnOrder { get; set; }

    /// <summary>Called with the column order after the reader changed it.</summary>
    public Callback<IReadOnlyList<string>> OnColumnOrderChange { get; set; }


    /// <summary>The columns grouped by, outermost first. Setting it hands that axis over.</summary>
    public IReadOnlyList<string>? Grouped { get; set; }

    /// <summary>Called with the grouping after the reader changed it.</summary>
    public Callback<IReadOnlyList<string>> OnGroupedChange { get; set; }


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

    /// <summary>
    ///     Describes the grid's columns, ending the chain:
    ///     <c>Ui.DataGrid.Rows(_rows)[c =&gt; [ c.Field(r =&gt; r.Name), c.Column()[ … ] ]]</c>.
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
    ///         to infer from — a bare <c>Ui.Column.Field(…)</c> written as a flat child has nothing.
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
    // REQUIRED chain step (RASK001), which would have made `Ui.DataGrid.Data(rows)` a pending state
    // rather than a finished chain — every later step then failing to infer its type arguments. Same
    // reason SortDescending is bool? rather than bool.
    private int Paging => PageSize ?? 0;

    private bool PageControlled => Page is not null;

    private bool SortControlled =>
        Sort is not null || OnSortChange.HasValue;

    private bool GroupControlled =>
        Grouped is not null || OnGroupedChange.HasValue;

    private bool HideControlled =>
        HiddenColumns is not null || OnHiddenColumnsChange.HasValue;

    private bool OrderControlled =>
        ColumnOrder is not null || OnColumnOrderChange.HasValue;

    private int CurrentPage => Page ?? _page;

    private string? CurrentSort => SortControlled ? Sort : _sortField;

    private bool CurrentSortDescending => SortControlled ? SortDescending is true : _sortDescending;

    private IReadOnlyList<string> CurrentGrouped => GroupControlled ? Grouped ?? [] : _grouped;

    private IReadOnlyList<string> CurrentHidden => HideControlled ? HiddenColumns ?? [] : _hidden;

    private IReadOnlyList<string> CurrentOrder => OrderControlled ? ColumnOrder ?? [] : _order;

    // Reordering is offered wherever the chooser is, or wherever the parent is driving the order.
    private bool ReorderEnabled => ColumnChooser is true || OrderControlled;

    private bool Expandable => Detail is not null;

    private bool SelectionEnabled => Selected is not null || OnSelectionChange.HasValue;

    private bool Busy => Loading is true;

    private int LeadingCells => (SelectionEnabled ? 1 : 0) + (Expandable ? 1 : 0);

    // ---- lifecycle: the awaited source ----------------------------------------------------------

    /// <inheritdoc />
    protected override Task OnMount() => FetchAsync();

    /// <inheritdoc />
    protected override Task OnUpdated() => FetchAsync();

    // ---- columns --------------------------------------------------------------------------------

    /// <summary>
    ///     Opens a column bound to a member of the row: its identity, its header and its cell value.
    /// </summary>
    /// <remarks>
    ///     Reached as <c>c.Field(p =&gt; p.Name)</c> inside the grid's column factory. An instance method
    ///     rather than an entry, and that is the whole point: the grid's own type argument is already
    ///     fixed here, so <c>p</c> has a type — where <c>Ui.Column.Field(p =&gt; p.Name)</c> written as a
    ///     flat child would have nothing to infer one from.
    /// </remarks>
    /// <param name="field">The member this column is about.</param>
    public UiColumn<T> Field(Expression<Func<T, object?>> field) => Ui.Column.Field(field);

    /// <summary>
    ///     Opens a column bound to no member — an actions column, or one computed from the whole row.
    /// </summary>
    /// <remarks>
    ///     It has no field token, so it can be shown but never sorted, grouped, hidden or reordered by
    ///     name. Give it a <see cref="UiColumn{T}.Cell" /> and a <see cref="UiColumn{T}.Title" />.
    /// </remarks>
    public UiColumn<T> Column() => Ui.Column.Of<T>();
}
