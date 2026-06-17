namespace Rask.Core.Tables;

// User-supplied definition of one table column. TableModel is fully presentational: it reads only
// Id / Header / Sortable to build sort-aware header cells — it never sorts the data or reads cell
// values itself. Value is an OPTIONAL convenience accessor for hosts that want to render cells from a
// generic column loop (`ctx.Headers`/`Columns`) instead of hand-writing each <td>; the table never
// invokes it.
public sealed class ColumnDef<T>
{
    // Stable column identity — used as the header key and to match ColumnSort entries in the
    // controlled sort state. Must be unique within a Columns list.
    public required string Id { get; init; }

    // Header label. Falls back to Id when null.
    public string? Header { get; init; }

    // Whether this column offers a sort affordance. When false, its HeaderCell.ToggleSort is a no-op
    // and HeaderCell.Direction is always null.
    public bool Sortable { get; init; } = true;

    // Optional cell-value accessor. TableModel never calls it; it exists purely so a host iterating
    // columns generically can pull a cell value without a per-column switch.
    public Func<T, object?>? Value { get; init; }
}
