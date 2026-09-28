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
