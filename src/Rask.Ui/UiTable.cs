using System.Text;
using Rask.Querying;

namespace Rask;

/// <summary>
/// A table of data: Flux's <c>flux:table</c>. Its parts are <see cref="UiTableColumns" /> holding one
/// <see cref="UiTableColumn" /> per heading, and <see cref="UiTableRows" /> holding a
/// <see cref="UiTableRow" /> of <see cref="UiTableCell" />s per record.
/// </summary>
/// <remarks>
/// <para>
/// It renders a column box, a scroll area inside it, and the <c>&lt;table&gt;</c> inside that: a table wider
/// or taller than its box scrolls there instead of pushing the page. Everything an element takes —
/// <c>Id</c>, <c>Class</c>, <c>Data</c>, <c>Aria</c>, the events — lands on the <c>&lt;table&gt;</c>; the
/// box takes <see cref="ContainerClass" />.
/// </para>
/// <para>
/// A cell does not wrap, so a long value widens its column; write <c>whitespace-normal</c> on the cell that
/// should. Columns follow their content until the table has a width: <c>.Class("w-full")</c> and a width on
/// every <see cref="UiTableColumn" /> but one hold them still across pages and sort orders.
/// </para>
/// <para>
/// Every part writes Flux's own class list. A class of the call site's that contradicts one of them — a layout, a
/// colour, a first cell's padding — is written with Tailwind's <c>!</c> (<c>table-auto!</c>, <c>ps-10!</c>), as
/// Flux's guide says.
/// </para>
/// </remarks>
public sealed partial class UiTable : UiElement, IUiHost
{
    // Flux's table, in Flux's order.
    private const string Base =
        "[:where(&)]:min-w-full table-fixed border-separate border-spacing-0 isolate text-zinc-800 whitespace-nowrap "
        + "[&_dialog]:whitespace-normal [&_[popover]]:whitespace-normal";

    // A bleeding table moves its first and last column in by the distance it bled out — on the table, once, as Flux does.
    private const string BleedEdges =
        "[&_[data-ui-column]:first-child]:ps-[var(--ui-bleed,1.5rem)] "
        + "[&_[data-ui-cell]:first-child]:ps-[var(--ui-bleed,1.5rem)] "
        + "[&_[data-ui-column]:last-child]:pe-[var(--ui-bleed,1.5rem)] "
        + "[&_[data-ui-cell]:last-child]:pe-[var(--ui-bleed,1.5rem)]";

    // The pager under the rows keeps its height when the box is given one to scroll within, as Flux's does.
    private const string Container = "flex flex-col *:data-ui-pagination:shrink-0";

    // The card's bleed contract (UiCard, UiCardBody): --ui-bleed sideways, and — where the table is the first
    // or last thing in the box — --ui-bleed-top / -bottom to that edge, rounded by --ui-bleed-*-radius. A box
    // that states only --ui-bleed, or nothing, bleeds sideways alone.
    private const string Bleeding =
        "-mx-[var(--ui-bleed,1.5rem)] "
        + "first:-mt-[var(--ui-bleed-top,0px)] first:rounded-t-[var(--ui-bleed-top-radius,0px)] "
        + "last:-mb-[var(--ui-bleed-bottom,0px)] last:rounded-b-[var(--ui-bleed-bottom-radius,0px)]";

    // `relative` departs from Flux (owner, 2026-10-14): Flux's frame is static, so an absolutely placed child of a
    // cell — a checkbox's hidden input, a hidden heading — escapes it and the whole page scrolls sideways on a phone.
    private const string ScrollArea = "relative block overflow-auto";

    private static readonly UiPartMarker Marker = new("ui-table");

    private IQueryState? _query;

    /// <summary>
    ///     Runs the row dividers through the horizontal padding of the box the table sits in, while the first
    ///     and last columns stay in line with that box's content.
    /// </summary>
    /// <remarks>
    ///     The distance is <c>--ui-bleed</c>, 1.5rem unless the box says otherwise: a box of yours states it
    ///     beside its padding — <c>p-4 [--ui-bleed:1rem]</c>. A <see cref="UiCard" /> states it and the rest of
    ///     its bleed variables, so a table that opens or closes a card or its body reaches that edge too.
    /// </remarks>
    public bool? Bleed { get; set; }

    /// <summary>The pager for this table's rows, shown under them and outside the scroll area.</summary>
    public Component? Paginate { get; set; }

    /// <summary>Classes for the box around the table — a height to scroll within, such as <c>max-h-80</c>.</summary>
    public string? ContainerClass { get; set; }

    /// <summary>
    ///     Draws the table from <paramref name="query" />: skeleton rows while it loads, and a row saying there is
    ///     nothing when it found nothing. The headings stay throughout.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Not Flux's: an addition the owner approved (2026-10-21). The first load draws a skeleton row per row
    ///         the pager says the page holds. Every later one — a page, a sort, a filter, a tab — turns the rows on
    ///         screen into skeleton rows at the press, in the browser, at their own heights, and the new rows take
    ///         their place in one step when they are there: nothing under the table moves.
    ///     </para>
    ///     <para>
    ///         The rows are still yours, built from <c>query.Data</c>. A control elsewhere on the page that loads
    ///         other rows names the table: <c>.Aria("controls", "orders")</c> beside <c>Ui.Table.Id("orders")</c>.
    ///     </para>
    /// </remarks>
    /// <param name="query">The query whose result the rows are drawn from.</param>
    /// <typeparam name="TResult">What the query returns.</typeparam>
    /// <returns>This table.</returns>
    public UiTable Of<TResult>(Query<TResult> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        _query = query;
        UiFilterTables.Built(this);
        return this;
    }

    /// <summary>
    ///     As <see cref="Of{TResult}(Query{TResult})" />, waiting for <paramref name="also" /> too: skeleton rows while
    ///     either loads — a query that says which columns to show, say — and the rows, their count and the empty row
    ///     from <paramref name="rows" />.
    /// </summary>
    /// <param name="rows">The query whose result the rows are drawn from.</param>
    /// <param name="also">A query the rows wait for.</param>
    /// <typeparam name="TRows">What the rows' query returns.</typeparam>
    /// <typeparam name="TAlso">What the other query returns.</typeparam>
    /// <returns>This table.</returns>
    public UiTable Of<TRows, TAlso>(Query<TRows> rows, Query<TAlso> also)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(also);
        return Waiting(new UiTableQueries(rows, also));
    }

    /// <summary>As <see cref="Of{TRows, TAlso}(Query{TRows}, Query{TAlso})" />, waiting for two other queries.</summary>
    /// <param name="rows">The query whose result the rows are drawn from.</param>
    /// <param name="also">A query the rows wait for.</param>
    /// <param name="andAlso">Another query the rows wait for.</param>
    /// <typeparam name="TRows">What the rows' query returns.</typeparam>
    /// <typeparam name="TAlso">What the second query returns.</typeparam>
    /// <typeparam name="TAndAlso">What the third query returns.</typeparam>
    /// <returns>This table.</returns>
    public UiTable Of<TRows, TAlso, TAndAlso>(Query<TRows> rows, Query<TAlso> also, Query<TAndAlso> andAlso)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(also);
        ArgumentNullException.ThrowIfNull(andAlso);
        return Waiting(new UiTableQueries(rows, also, andAlso));
    }

    private UiTable Waiting(UiTableQueries queries)
    {
        _query = queries;
        UiFilterTables.Built(this);
        return this;
    }

    /// <summary>
    ///     Draws the table from rows the page loads itself, as <see cref="Of{TResult}" /> draws it from a query:
    ///     skeleton rows while <paramref name="loading" />, and a row saying there is nothing when it holds none.
    /// </summary>
    /// <remarks>
    ///     Not Flux's, as <see cref="Of{TResult}" /> is not: <c>Ui.Table.Loading(_rows is null)[…]</c>. A skeleton row per
    ///     row the pager says the page holds (15 with no pager), a bar per heading.
    /// </remarks>
    /// <param name="loading">Whether the rows are still on their way.</param>
    /// <returns>This table.</returns>
    public UiTable Loading(bool loading)
    {
        var load = _query as UiTableLoad ?? new UiTableLoad();
        load.IsLoading = loading;
        _query = load;
        UiFilterTables.Built(this);
        return this;
    }

    /// <inheritdoc />
    /// <remarks>None: the table renders as its box, with the <c>&lt;table&gt;</c> inside.</remarks>
    protected override string? TagName => null;

    /// <inheritdoc />
    protected override string? ResolveClass() => UiClass.Compose(Base, Bleed is true ? BleedEdges : "", Class);

    private protected override IReadOnlyDictionary<string, string?> ResolveData() => Marker.With(Data);

    /// <inheritdoc />
    protected override Component? Render()
    {
        // Both marks in one step: a second Data step replaces the first, and a bleeding table drawn from a query lost
        // the mark its cells pad against.
        var marks = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (Bleed is true)
        {
            marks["ui-table-bleed"] = "";
        }

        if (_query is { } query)
        {
            UiTableSkeleton.Fill(query, Children, Paginate);
            marks["rask-skeleton"] = UiTableSkeleton.Word(query);
        }

        var box = Div.Class(Container, Bleed is true ? Bleeding : "", ContainerClass);
        if (marks.Count > 0)
        {
            box = box.Data(marks);
        }

        return box[
            Div.Class(ScrollArea)[
                HostedElement.Tag("table").Owner(this)[Children ?? []]
            ],
            Paginate
        ];
    }

    void IUiHost.WriteHostAttributes(StringBuilder sb) => WriteAttributes(sb);
}
