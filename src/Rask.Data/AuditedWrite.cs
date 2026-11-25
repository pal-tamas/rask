namespace Rask.Data;

/// <summary>
///     One save of an <see cref="Audited" /> aggregate, as an <see cref="AuditFormat{TRow, TUserId}" /> is handed it to
///     write the app's own audit row: what was done to which aggregate, everything it held and holds, what changed, by whom
///     and when.
/// </summary>
/// <typeparam name="TUserId">What the app's users are keyed by: a <see cref="Guid" />, an <see cref="int" /> or a <see cref="long" />.</typeparam>
/// <remarks>
///     Every value is named by its path — <c>Number</c>, <c>Km.Start</c>, <c>Lines[1].Quantity</c> — and is the value as
///     the database stores it (a value object's stored value), in the order of the aggregate's columns, then its rows'.
/// </remarks>
public sealed class AuditedWrite<TUserId>
    where TUserId : struct
{
    internal AuditedWrite(
        Type aggregate, AuditAction action, IReadOnlyList<AuditValue> key, IReadOnlyList<AuditValue> before,
        IReadOnlyList<AuditValue> after, IReadOnlySet<string> changed, TUserId? user, DateTime at)
    {
        Aggregate = aggregate;
        Action = action;
        Key = key;
        Before = before;
        After = after;
        Changed = changed;
        User = user;
        At = at;
    }

    /// <summary>The aggregate's type: <c>typeof(DeliveryNote)</c>.</summary>
    public Type Aggregate { get; }

    /// <summary>What the save did to it; a soft delete is a <see cref="AuditAction.Delete" />.</summary>
    public AuditAction Action { get; }

    /// <summary>The aggregate's key — one the database generates as it was written.</summary>
    public IReadOnlyList<AuditValue> Key { get; }

    /// <summary>Every value the aggregate held as the save began, its rows' too; empty for an <see cref="AuditAction.Insert" />.</summary>
    public IReadOnlyList<AuditValue> Before { get; }

    /// <summary>
    ///     Every value the aggregate holds once the save went out, its rows' too — the values the database generated
    ///     among them; empty for a hard <see cref="AuditAction.Delete" />.
    /// </summary>
    public IReadOnlyList<AuditValue> After { get; }

    /// <summary>
    ///     The paths whose value the save changed — a value that became null, or stopped being null, among them; every
    ///     path of a row it added or deleted.
    /// </summary>
    public IReadOnlySet<string> Changed { get; }

    /// <summary>
    ///     The user the work was for — a <c>Current.UseUser</c> scope's, or the signed-in principal's id claim read as
    ///     <typeparamref name="TUserId" />; null when it was for nobody (a job, a hosted service, startup).
    /// </summary>
    public TUserId? User { get; }

    /// <summary>When the save was made, in UTC (Rask's clock, so <c>Clock.Fake</c> moves it).</summary>
    public DateTime At { get; }
}
