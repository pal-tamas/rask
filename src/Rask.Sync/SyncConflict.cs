namespace Rask.Sync;

/// <summary>What kind of loss a <see cref="SyncConflict" /> is reporting.</summary>
public enum SyncConflictKind
{
    /// <summary>An incoming op replaced a value another device had written. Their edit is gone.</summary>
    Overwritten,

    /// <summary>An incoming op lost to a newer value already held. The arriving edit is gone.</summary>
    Discarded,

    /// <summary>A delete won over field edits another device had made. Those edits are no longer visible.</summary>
    DeleteHidEdits,

    /// <summary>An edit landed after another device deleted the row, so the row is visible again.</summary>
    EditRevivedDeleted,
}

/// <summary>
///     A record that merging discarded somebody's work.
/// </summary>
/// <remarks>
///     <para>
///         Last-writer-wins <b>loses data by design</b>. Two people edit the same field while offline, one
///         edit survives, and without this the other simply disappears — no error, no trace, and the person
///         who lost it usually finds out days later, if ever. That is the single most damaging thing an
///         offline-first system can do, and it is not fixed by choosing a cleverer rule: something has to
///         lose. What can be fixed is whether anyone is told.
///     </para>
///     <para>
///         So the engine reports rather than resolves. Every merge that discarded a value another node
///         wrote produces one of these, carrying both values and both stamps, and it is the application's
///         decision what to do with it — surface it, keep an audit trail, or offer the losing value back.
///         Merging is still fully automatic; nothing blocks on a human.
///     </para>
///     <para>
///         Writes from the <em>same</em> node are not conflicts — that is just a device overwriting its own
///         earlier value — and neither is one node writing the value another node already had.
///     </para>
/// </remarks>
/// <param name="Kind">What was lost.</param>
/// <param name="Entity">The entity type.</param>
/// <param name="Id">The row.</param>
/// <param name="Field">The field, or <c>null</c> when the conflict is about the row as a whole.</param>
/// <param name="WinningValue">The raw JSON value that survived, or <c>null</c> for a delete.</param>
/// <param name="LosingValue">The raw JSON value that was discarded, or <c>null</c> for a delete.</param>
/// <param name="WinningStamp">The stamp that won.</param>
/// <param name="LosingStamp">The stamp that lost.</param>
public sealed record SyncConflict(
    SyncConflictKind Kind,
    string Entity,
    Guid Id,
    string? Field,
    string? WinningValue,
    string? LosingValue,
    HlcTimestamp WinningStamp,
    HlcTimestamp LosingStamp)
{
    /// <summary>The node whose work survived.</summary>
    public string WinningNode => WinningStamp.NodeId;

    /// <summary>The node whose work was discarded.</summary>
    public string LosingNode => LosingStamp.NodeId;
}
