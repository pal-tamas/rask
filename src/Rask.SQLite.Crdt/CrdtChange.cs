namespace Rask.SQLite.Crdt;

/// <summary>
///     One row of cr-sqlite's change feed: a single column of a single row being set, stamped with who
///     set it and when.
/// </summary>
/// <remarks>
///     <para>
///         This is the unit of replication, and its shape is why merging is conflict-free per <b>column</b>
///         rather than per row: <see cref="ColumnName" /> names the column, so two devices changing
///         different columns of the same record produce changes that do not compete.
///     </para>
///     <para>
///         <see cref="SiteId" /> identifies the replica that made the change and <see cref="DbVersion" />
///         is that replica's monotonic counter, so "everything from peer X after version N" is the whole
///         of what a sync has to ask for. Applying a change twice is harmless, which is what makes
///         re-sending safe after a failed upload.
///     </para>
/// </remarks>
/// <param name="Table">The table the change belongs to.</param>
/// <param name="PrimaryKey">The row's packed primary key, opaque to callers.</param>
/// <param name="ColumnName">The column being set, or a sentinel for a delete.</param>
/// <param name="Value">The new value, or <c>null</c>.</param>
/// <param name="ColumnVersion">Per-column version, used to decide which write wins.</param>
/// <param name="DbVersion">The originating replica's monotonic version at the time of the change.</param>
/// <param name="SiteId">The originating replica's identity.</param>
/// <param name="CausalLength">Tracks whether the row is currently present or deleted.</param>
/// <param name="Sequence">Orders changes made within one transaction.</param>
public sealed record CrdtChange(
    string Table,
    byte[] PrimaryKey,
    string ColumnName,
    object? Value,
    long ColumnVersion,
    long DbVersion,
    byte[] SiteId,
    long CausalLength,
    long Sequence);
