namespace Rask.Sync;

/// <summary>A row as the merge currently sees it.</summary>
/// <param name="Entity">The entity type.</param>
/// <param name="Id">The row's key.</param>
/// <param name="Values">Field name to raw JSON value.</param>
/// <param name="LastModified">The newest stamp among the row's fields.</param>
public sealed record SyncRow(
    string Entity,
    Guid Id,
    IReadOnlyDictionary<string, string> Values,
    HlcTimestamp LastModified);

/// <summary>
///     The materialised result of replaying an operation log.
/// </summary>
/// <remarks>
///     <para>
///         Three properties hold, and they are what make a log safe to sync over storage that promises
///         nothing about ordering or delivery:
///     </para>
///     <list type="bullet">
///         <item>
///             <b>Order-independent.</b> Applying the same ops in any order gives the same state, because
///             every field keeps the value with the highest stamp rather than the one that arrived last.
///         </item>
///         <item>
///             <b>Idempotent.</b> Applying an op twice changes nothing, so a retry after a failed upload,
///             or a peer re-listing objects it already read, costs nothing and risks nothing.
///         </item>
///         <item>
///             <b>Convergent.</b> Two replicas that have seen the same set of ops hold identical state,
///             whatever route those ops took to get there.
///         </item>
///     </list>
///     <para>
///         Together they mean a client never has to know what it already sent, never has to coordinate
///         with a peer, and never has to be right about the order — which is exactly what removes the need
///         for a server.
///     </para>
///     <para>Not thread-safe; apply from one place, as a replica would.</para>
/// </remarks>
public sealed class SyncState
{
    private readonly Dictionary<(string Entity, Guid Id), Row> _rows = [];

    /// <summary>How many rows are present, including ones currently deleted.</summary>
    public int Count => _rows.Count;

    /// <summary>
    ///     Applies one op and reports anything it discarded. Safe to call with an op already applied.
    /// </summary>
    public IReadOnlyList<SyncConflict> Apply(SyncOp op)
    {
        ArgumentNullException.ThrowIfNull(op);

        List<SyncConflict>? conflicts = null;
        ApplyCore(op, ref conflicts);
        return (IReadOnlyList<SyncConflict>?)conflicts ?? [];
    }

    /// <summary>Applies many ops and reports everything discarded across all of them.</summary>
    public IReadOnlyList<SyncConflict> Apply(IEnumerable<SyncOp> ops)
    {
        ArgumentNullException.ThrowIfNull(ops);

        List<SyncConflict>? conflicts = null;
        foreach (var op in ops)
        {
            ApplyCore(op, ref conflicts);
        }

        return (IReadOnlyList<SyncConflict>?)conflicts ?? [];
    }

    /// <summary>Reads a row, or <c>null</c> if it is absent or currently deleted.</summary>
    public SyncRow? Get(string entity, Guid id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entity);

        return _rows.TryGetValue((entity, id), out var row) && !row.IsDeleted
            ? new SyncRow(entity, id, row.Values(), row.LastModified)
            : null;
    }

    /// <summary>Whether the row exists and is currently deleted.</summary>
    public bool IsDeleted(string entity, Guid id) =>
        _rows.TryGetValue((entity, id), out var row) && row.IsDeleted;

    /// <summary>Every visible row of <paramref name="entity" />, ordered by key for a stable read.</summary>
    public IReadOnlyList<SyncRow> All(string entity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entity);

        return _rows
            .Where(kv => kv.Key.Entity == entity && !kv.Value.IsDeleted)
            .OrderBy(kv => kv.Key.Id)
            .Select(kv => new SyncRow(entity, kv.Key.Id, kv.Value.Values(), kv.Value.LastModified))
            .ToList();
    }

    private void ApplyCore(SyncOp op, ref List<SyncConflict>? conflicts)
    {
        if (!_rows.TryGetValue((op.Entity, op.Id), out var row))
        {
            row = new Row();
            _rows[(op.Entity, op.Id)] = row;
        }

        if (op.Deleted)
        {
            ApplyDelete(op, row, ref conflicts);
            return;
        }

        if (op.Set is null)
        {
            return;
        }

        // An edit that lands after another node's delete brings the row back. Detected before the fields
        // are written, while the previous delete stamp is still the one to compare against.
        var revived = row.DeletedAt is { } deleted
                      && deleted.NodeId != op.Stamp.NodeId
                      && op.Stamp > deleted
                      && row.IsDeleted;

        foreach (var (field, value) in op.Set)
        {
            ApplyField(op, row, field, value, ref conflicts);
        }

        if (revived && !row.IsDeleted)
        {
            Add(ref conflicts, new SyncConflict(
                SyncConflictKind.EditRevivedDeleted, op.Entity, op.Id, null, null, null,
                op.Stamp, row.DeletedAt!.Value));
        }
    }

    private static void ApplyField(
        SyncOp op, Row row, string field, string value, ref List<SyncConflict>? conflicts)
    {
        if (!row.Fields.TryGetValue(field, out var existing))
        {
            row.Fields[field] = (value, op.Stamp);
            return;
        }

        var comparison = op.Stamp.CompareTo(existing.Stamp);

        // Equal stamps mean the very same event — a duplicate delivery. Doing nothing is what makes
        // applying a log twice indistinguishable from applying it once.
        if (comparison == 0)
        {
            return;
        }

        if (comparison > 0)
        {
            // Only a real loss is worth reporting: a node replacing its own earlier value is not a
            // conflict, and neither is writing the value that was already there.
            if (existing.Stamp.NodeId != op.Stamp.NodeId && !string.Equals(existing.Value, value, StringComparison.Ordinal))
            {
                Add(ref conflicts, new SyncConflict(
                    SyncConflictKind.Overwritten, op.Entity, op.Id, field,
                    value, existing.Value, op.Stamp, existing.Stamp));
            }

            row.Fields[field] = (value, op.Stamp);
            return;
        }

        if (existing.Stamp.NodeId != op.Stamp.NodeId && !string.Equals(existing.Value, value, StringComparison.Ordinal))
        {
            Add(ref conflicts, new SyncConflict(
                SyncConflictKind.Discarded, op.Entity, op.Id, field,
                existing.Value, value, existing.Stamp, op.Stamp));
        }
    }

    private static void ApplyDelete(SyncOp op, Row row, ref List<SyncConflict>? conflicts)
    {
        if (row.DeletedAt is { } existing && op.Stamp <= existing)
        {
            return;
        }

        // Field writes from another node that this delete now hides. Reported once for the row rather than
        // once per field: to a user, one record disappeared.
        var hidden = row.Fields
            .Where(f => f.Value.Stamp < op.Stamp && f.Value.Stamp.NodeId != op.Stamp.NodeId)
            .ToList();

        row.DeletedAt = op.Stamp;

        if (hidden.Count > 0 && row.IsDeleted)
        {
            var newest = hidden.MaxBy(f => f.Value.Stamp);
            Add(ref conflicts, new SyncConflict(
                SyncConflictKind.DeleteHidEdits, op.Entity, op.Id, null, null, newest.Value.Value,
                op.Stamp, newest.Value.Stamp));
        }
    }

    private static void Add(ref List<SyncConflict>? conflicts, SyncConflict conflict) =>
        (conflicts ??= []).Add(conflict);

    private sealed class Row
    {
        public Dictionary<string, (string Value, HlcTimestamp Stamp)> Fields { get; } =
            new(StringComparer.Ordinal);

        public HlcTimestamp? DeletedAt { get; set; }

        /// <summary>
        ///     A delete hides the row only until something newer is written to it. That is what makes
        ///     delete and edit commute: whichever carries the higher stamp decides, no matter which arrived
        ///     first.
        /// </summary>
        public bool IsDeleted =>
            DeletedAt is { } deleted && !Fields.Values.Any(f => f.Stamp > deleted);

        public HlcTimestamp LastModified =>
            Fields.Count == 0
                ? DeletedAt ?? HlcTimestamp.MinValue
                : Fields.Values.Max(f => f.Stamp);

        public IReadOnlyDictionary<string, string> Values() =>
            Fields.ToDictionary(f => f.Key, f => f.Value.Value, StringComparer.Ordinal);
    }
}
