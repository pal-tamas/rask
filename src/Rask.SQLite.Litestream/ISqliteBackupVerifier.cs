namespace Rask.SQLite.Litestream;

/// <summary>
/// Proves the backup is <b>restorable</b>, not merely that the replicator is running: writes a sentinel
/// into the live database, waits for replication to carry it, restores to a throwaway path, and checks
/// the sentinel came back.
/// <para>
/// Registered by <see cref="LitestreamServiceCollectionExtensions.AddRaskSqliteLitestream"/> whether or
/// not the schedule is enabled, so an operator surface can run a pass on demand. Every pass runs a real
/// restore and downloads a real database — see <see cref="LitestreamVerificationOptions"/> before wiring
/// one to anything that can be called repeatedly.
/// </para>
/// </summary>
public interface ISqliteBackupVerifier
{
    /// <summary>
    /// Runs one verification pass and publishes the result to <see cref="LitestreamStatus.Verification"/>.
    /// Does not throw for a failed backup — the outcome <i>is</i> the return value, and a backup problem
    /// must never take down the app it protects. Only cancellation propagates.
    /// </summary>
    Task<LitestreamVerificationStatus> VerifyAsync(CancellationToken cancellationToken = default);
}
