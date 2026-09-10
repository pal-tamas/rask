using System.Linq.Expressions;
using System.Reflection;

namespace Rask.Ui;

/// <summary>
/// One column of a <see cref="UiDataGrid{T,TKey}" />: what it is titled, how each cell renders, and whether it
/// can be sorted, grouped, hidden or reordered.
/// </summary>
/// <remarks>
/// <para>
/// Built from inside the grid's column factory — <c>UiDataGrid.Data(rows)[c =&gt; [ c.Field(p =&gt; p.Name)
/// … ]]</c> — because that is what fixes the row type. A column written as a flat child of the grid has
/// nothing to infer its lambda's parameter from and does not compile; see the grid's column indexer.
/// </para>
/// <para>
/// It renders nothing itself. The grid reads its properties and draws the header, the cells, the footer
/// and the card line from them, which is why a column is a component at all: it wants a chain, a
/// <c>Key</c>, and the ability to be <see langword="null" /> in one arm of a conditional.
/// </para>
/// </remarks>
/// <typeparam name="T">The row type.</typeparam>
public sealed partial class UiColumn<T> : Component
{
    // Everything read off Field, recomputed whenever Field is assigned a different tree.
    //
    // NOT cached once per instance, and that distinction is the whole reason this is a setter rather
    // than a lazy property: a column is entry-built, so the SAME instance is handed back on every
    // render, and a one-shot cache would pin the first frame's field forever — a column whose
    // expression changed between renders would keep sorting and reading the old member. The C# compiler
    // allocates a fresh tree per evaluation, so the reference check below re-derives once per render
    // and no more.
    private Expression<Func<T, object?>>? _field;
    private string? _name;
    private PropertyInfo[] _path = [];

    /// <summary>
    ///     The column's stable identity: <c>Field = p =&gt; p.Category</c> names this column
    ///     <c>"category"</c>, and supplies each cell's value when <see cref="Value" /> says nothing else.
    /// </summary>
    /// <remarks>
    ///     That token is what <see cref="UiDataGrid{T,TKey}.OnSortChange" />,
    ///     <see cref="UiDataGrid{T,TKey}.OnGroupedChange" />, <see cref="UiDataGrid{T,TKey}.HiddenColumns" /> and
    ///     <see cref="UiDataGrid{T,TKey}.ColumnOrder" /> speak in. A column with no <c>Field</c> has no token,
    ///     so it can be shown but never sorted, grouped, hidden or reordered by name — which is right for
    ///     an actions column and wrong for anything else.
    ///     <para>
    ///         An <see cref="Expression{TDelegate}" /> rather than a <see cref="Func{T, TResult}" />: only
    ///         an expression tree can be read for the member's NAME, and only an expression tree can be
    ///         translated into <c>ORDER BY</c> when the grid is driving an <see cref="IQueryable{T}" />.
    ///     </para>
    /// </remarks>
    public Expression<Func<T, object?>>? Field
    {
        get => _field;
        set
        {
            if (ReferenceEquals(_field, value))
            {
                return;
            }

            _field = value;
            (_name, _path) = Describe(value);
        }
    }

    // The member chain behind Field, plus the camelCased token naming its last member
    // ("p => p.Supplier.Name" -> "name"). Reflection rather than Expression.Compile(): compiling needs
    // dynamic code, which is exactly what a trimmed browser-WASM publish has none of — and the framework
    // already reads its bound fields this way for the same reason (see ExpressionAccessor).
    //
    // Reflection over a property the EXPRESSION names is trim-safe without an annotation: the compiler
    // emits a metadata token for each getter in the tree, so the trimmer can see the reference and keeps
    // them.
    private static (string? Name, PropertyInfo[] Path) Describe(Expression<Func<T, object?>>? field)
    {
        if (field is null)
        {
            return (null, []);
        }

        var body = field.Body;

        // `p => p.Category` against an object? return type is lowered to Convert(p.Category, object),
        // so the boxing conversion has to come off before the member is visible.
        if (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } u)
        {
            body = u.Operand;
        }

        var chain = new List<PropertyInfo>();
        while (body is MemberExpression { Member: PropertyInfo property } member)
        {
            chain.Insert(0, property);
            body = member.Expression!;
        }

        // Anything that is not a chain of properties off the row — a method call, arithmetic, a literal
        // — is refused rather than rendered blank. Field is the column's IDENTITY as well as its value,
        // and a computed one has no member to name, so it could be neither sorted nor grouped and would
        // silently show nothing. Value says "compute this" and is the step that was wanted.
        if (chain.Count == 0 || body is not ParameterExpression)
        {
            throw new ArgumentException(
                $"A column's Field must name a member of {typeof(T).Name} — 'p => p.Name' or "
                + $"'p => p.Supplier.Name'. It is the column's identity as well as its value, so a "
                + $"computed expression has nothing to sort, group or hide it by. Use Value for a "
                + $"computed cell, or Cell for one with markup. Got: {field}",
                nameof(field));
        }

        var last = chain[^1].Name;
        var name = char.IsUpper(last[0]) ? char.ToLowerInvariant(last[0]) + last[1..] : last;
        return (name, [.. chain]);
    }

    /// <summary>The column's header text.</summary>
    /// <remarks>
    ///     Also the label the card layout puts in front of the cell below <c>sm</c>, so a column with no
    ///     title reads as an unlabelled line there.
    /// </remarks>
    public string? Title { get; set; }

    /// <summary>How to read each cell's value from its row, when it is not simply the field.</summary>
    /// <remarks>
    ///     The result is rendered as encoded text. Use it for a computed value; use <see cref="Cell" />
    ///     when the cell needs markup.
    /// </remarks>
    public Fn<T, object?>? Value { get; set; }

    /// <summary>Custom markup for the cell, when plain text is not enough.</summary>
    public Fn<T, Component>? Cell { get; set; }

    /// <summary>Lets the reader sort by this column.</summary>
    public bool? Sortable { get; set; }

    /// <summary>The key this column sorts by in memory, when it is not the displayed value.</summary>
    public Fn<T, IComparable?>? SortKey { get; set; }

    /// <summary>
    ///     What this column orders by in a query, when it differs from <see cref="Field" />.
    /// </summary>
    /// <remarks>
    ///     An <see cref="Expression{TDelegate}" /> because only an expression tree reaches <c>ORDER BY</c>;
    ///     a <see cref="Func{T, TResult}" /> would drag the whole table into memory to sort it there, which
    ///     is exactly what driving an <see cref="IQueryable{T}" /> exists to avoid. <see cref="SortKey" />
    ///     is the in-memory equivalent and is ignored in query mode.
    /// </remarks>
    public Expression<Func<T, object?>>? SortBy { get; set; }

    /// <summary>Lets the reader group by this column.</summary>
    public bool? Groupable { get; set; }

    /// <summary>The value rows are banded by when grouped, when it is not the displayed value.</summary>
    public Fn<T, object?>? GroupKey { get; set; }

    /// <summary>Custom markup for a band's header row.</summary>
    /// <param>The band's key and its rows.</param>
    public Fn<object?, IReadOnlyList<T>, Component>? GroupHeader { get; set; }

    /// <summary>A summary value shown in the column's footer, computed over every row.</summary>
    /// <remarks>
    ///     Over EVERY row, not the page — a footer that totalled the twenty rows on screen while calling
    ///     itself a total is a number nobody can tell is wrong. The cost of that lands on a grid driven
    ///     by an <see cref="IQueryable{T}" />: giving any column a footer makes the grid fetch the whole
    ///     set to compute it, where a grid without one only ever fetches a page. Worth knowing before
    ///     putting a footer on a grid over a large table.
    /// </remarks>
    public Fn<IReadOnlyList<T>, object?>? Footer { get; set; }

    /// <summary>Custom markup for the column's footer.</summary>
    public Fn<IReadOnlyList<T>, Component>? FooterCell { get; set; }

    /// <summary>Whether the column chooser may hide this column. Unset is yes.</summary>
    public bool? Hideable { get; set; }

    /// <summary>Whether the column chooser may move this column. Unset is yes.</summary>
    public bool? Reorderable { get; set; }

    /// <summary>
    ///     Whether <see cref="UiDataGrid{T,TKey}.OnRowClick" /> fires from this column's cells. Unset is AUTO:
    ///     a plain-text column is clickable, a <see cref="Cell" /> column is not.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         That asymmetry is a safety rule rather than a style. The grid attaches the row-click handler
    ///         to the cells, and the client cancels the default action of any click it dispatches — so
    ///         under a handler a checkbox never fires <c>change</c>, an <c>&lt;a href&gt;</c> never
    ///         navigates, and a bare <c>&lt;button&gt;</c> (which defaults to <c>type=submit</c>) swallows
    ///         the click instead. Every one of those failures is silent.
    ///     </para>
    ///     <para>
    ///         A text cell can contain none of them and is always safe. A <see cref="Cell" /> is exactly
    ///         where an author puts a link or a button, so it opts out by default. Set <c>true</c> to make
    ///         a non-interactive template (a badge, an icon) clickable anyway, or <c>false</c> to carve a
    ///         text column out.
    ///     </para>
    /// </remarks>
    public bool? RowClickable { get; set; }

    /// <summary>Extra classes for the header cell, the body cells and the footer cell alike.</summary>
    public string? Class { get; set; }

    internal bool HasFooter => Footer is not null || FooterCell is not null;

    internal bool IsRowClickable => RowClickable ?? Cell is null;

    internal bool CanHide => Hideable ?? true;

    internal bool CanReorder => Reorderable ?? true;

    internal string? FieldName => _name;

    internal Expression<Func<T, object?>>? OrderBy => SortBy ?? Field;

    // Walks the member chain Field named. A null anywhere along it reads as null rather than throwing,
    // so `p => p.Supplier.Name` on a row with no supplier renders an empty cell instead of taking the
    // page down.
    internal object? Read(T row)
    {
        if (Value is not null)
        {
            return Value.Value.Invoke(row);
        }

        object? value = row;
        foreach (var step in _path)
        {
            if (value is null)
            {
                return null;
            }

            value = step.GetValue(value);
        }

        return ReferenceEquals(value, row) ? null : value;
    }

    internal object? Band(T row) => GroupKey is { } group ? group.Invoke(row) : Read(row);

    // Ordering key for a band. Banding compares keys by equality, but the rows have to ARRIVE grouped,
    // and that ordering needs an IComparable — the same shape SortOf uses for a sorted column.
    internal IComparable? BandOrder(T row) => Band(row) as IComparable;

    internal IComparable? SortOf(T row) =>
        SortKey is { } sort ? sort.Invoke(row) : Read(row) as IComparable;

    internal Component Body(T row) =>
        Cell is { } cell && cell.Invoke(row) is { } built ? built : (Read(row)?.ToString() ?? "");

    internal Component Foot(IReadOnlyList<T> rows) =>
        FooterCell is { } cell && cell.Invoke(rows) is { } built ? built
        : Footer is { } foot ? (foot.Invoke(rows)?.ToString() ?? "")
        : "";

    /// <inheritdoc />
    /// <remarks>
    ///     Nothing. A column is read by its grid, never placed in the tree — rendering one on its own
    ///     would put a stray cell outside any table.
    /// </remarks>
    protected override Component? Render() => null;
}
