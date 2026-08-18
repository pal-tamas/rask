namespace Rask.SQLite.Litestream;

/// <summary>
/// What one restore-verification pass concluded. Three-valued on purpose: "the sentinel had not shipped
/// yet" and "the restore came back without it" are different facts, and only one of them is worth an alert.
/// </summary>
public enum LitestreamVerificationOutcome
{
    /// <summary>
    /// The restore produced a database containing the sentinel written just before it: the round trip is
    /// proven, and the backup was restorable at this moment.
    /// </summary>
    Verified,

    /// <summary>
    /// The restore worked but the sentinel had not been replicated inside the budget. Replication lag,
    /// not a broken backup — retry on the next pass. Paging on this is how a verification job gets
    /// turned off; the signal to watch is a <see cref="LitestreamVerificationStatus.LastVerifiedAt"/>
    /// that stops moving.
    /// </summary>
    Inconclusive,

    /// <summary>
    /// The round trip is broken: the restore itself failed (no replica, wrong prefix, rotated
    /// credentials), or it produced a database the sentinel was missing from. <b>This is the alert.</b>
    /// </summary>
    Failed,

    /// <summary>
    /// Nothing was verified because nothing could be: no <see cref="LitestreamOptions.DatabasePath"/> to
    /// write a sentinel into (<c>-config</c> mode can manage several databases, so there is no single one
    /// to probe), or that database does not exist locally yet.
    /// </summary>
    Skipped,
}

/// <summary>
/// The result of the most recent restore-verification pass — the answer to "is the backup
/// <i>restorable</i>?", as opposed to <see cref="LitestreamReplicationStatus"/>, which only answers "is
/// the replicator running?". A replica written to the wrong prefix, or one whose credentials were
/// rotated to read-only, keeps replication looking perfectly healthy and is only ever caught here.
/// </summary>
/// <param name="Outcome">What the most recent pass concluded.</param>
/// <param name="LastAttemptedAt">When the most recent pass finished, UTC — whatever it concluded.</param>
/// <param name="LastVerifiedAt">
/// When the backup was last <b>proven restorable</b>, UTC, or <c>null</c> if it never has been. This is
/// the field to alert on: it survives an inconclusive pass, so what it reports is the age of the last
/// good round trip rather than the outcome of the last attempt.
/// </param>
/// <param name="ReplicationLag">
/// How long the whole round trip took on the most recent successful pass — measured from just before the
/// sentinel write to the restored copy that contained it, so it includes the configured
/// <see cref="LitestreamVerificationOptions.ReplicationGrace"/> and the restore itself, not replication
/// alone. A value creeping towards <see cref="LitestreamVerificationOptions.Timeout"/> is the early
/// warning that passes are about to start coming back inconclusive.
/// </param>
/// <param name="LastError">Why the most recent pass was inconclusive or failed, or <c>null</c> if it was neither.</param>
public sealed record LitestreamVerificationStatus(
    LitestreamVerificationOutcome Outcome,
    DateTimeOffset LastAttemptedAt,
    DateTimeOffset? LastVerifiedAt,
    TimeSpan? ReplicationLag,
    string? LastError);
