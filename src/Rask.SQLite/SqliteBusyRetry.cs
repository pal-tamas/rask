using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace Rask.SQLite;

/// <summary>
/// The non-blocking, fair-interval busy-retry loop that owns all waiting for a contended write lock.
/// Runs a single SQLite statement through the raw <c>sqlite3</c> handle — so neither SQLite's own busy
/// handler nor Microsoft.Data.Sqlite's synchronous <c>Thread.Sleep</c> retry runs — and, on
/// <c>SQLITE_BUSY</c>/<c>SQLITE_LOCKED</c>, awaits a constant <see cref="SqliteBusyRetryOptions.PollInterval"/>
/// (yielding the thread) before retrying, giving up after <see cref="SqliteBusyRetryOptions.Timeout"/>.
/// </summary>
internal static class SqliteBusyRetry
{
    // requiresAutocommit: roll back any transaction open on the handle before EVERY attempt, not just
    // the first. Set it for BEGIN, the one statement that cannot run inside a transaction: a BEGIN
    // IMMEDIATE answered with an extended SQLITE_BUSY variant (BUSY_SNAPSHOT, BUSY_RECOVERY) can leave
    // the transaction open, and re-issuing the same BEGIN on the next pass then fails with the
    // non-retryable "cannot start a transaction within a transaction" instead of the contended lock it
    // actually met.
    //
    // requiresTransaction is that guard's mirror, for COMMIT — the one statement that cannot run
    // OUTSIDE a transaction. SQLite documents that a statement inside a multi-statement transaction
    // answered with SQLITE_BUSY (also FULL / IOERR / NOMEM / INTERRUPT) may be rolled back
    // automatically, and that sqlite3_get_autocommit is the only way to find out. Without this the
    // retry sleeps and blindly re-issues COMMIT into autocommit, which fails with the non-retryable
    // "cannot commit - no transaction is active" — the commit-side twin of #504, reported as #578.
    internal static async Task ExecAsync(
        sqlite3 handle,
        string sql,
        SqliteBusyRetryOptions options,
        TimeProvider timeProvider,
        CancellationToken cancellationToken,
        bool requiresAutocommit = false,
        bool requiresTransaction = false)
    {
        var startedAt = timeProvider.GetTimestamp();
        var attempt = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;

            // Read once per pass: it decides the precondition below and, if this attempt fails, it is the
            // state the diagnosis reports — the state *before* the statement ran, which is what separates
            // "arrived with a transaction" from "lost one while running".
            var autocommitAtEntry = raw.sqlite3_get_autocommit(handle);

            // The precondition is part of the attempt, so a rollback that is itself contended simply
            // costs this pass and is retried like any other busy result — it never runs `sql` on a
            // handle that is still mid-transaction.
            var rc = requiresAutocommit && autocommitAtEntry == 0
                ? raw.sqlite3_exec(handle, "ROLLBACK;")
                : raw.SQLITE_OK;

            if (rc == raw.SQLITE_OK)
            {
                rc = raw.sqlite3_exec(handle, sql);
                if (rc == raw.SQLITE_OK)
                {
                    return;
                }
            }

            // Only a contended lock is retryable; every other result code is a real error. Compare the
            // primary result code (low byte) so an extended BUSY/LOCKED variant is still treated as a
            // contended lock rather than misclassified as fatal.
            var primary = rc & 0xFF;
            if (primary != raw.SQLITE_BUSY && primary != raw.SQLITE_LOCKED)
            {
                throw Failure(handle, rc, attempt, autocommitAtEntry);
            }

            // Contended, and the transaction we were committing into is gone: SQLite rolled it back as
            // part of answering BUSY. Retrying the COMMIT can only produce the misleading
            // "no transaction is active"; the caller has to re-run the whole transaction instead.
            if (requiresTransaction && raw.sqlite3_get_autocommit(handle) != 0)
            {
                throw new SqliteTransactionRolledBackException(sql, attempt);
            }

            if (timeProvider.GetElapsedTime(startedAt) >= options.Timeout)
            {
                throw Failure(handle, rc, attempt, autocommitAtEntry);
            }

            // The thread is free here — this is the whole point (the wait is genuinely non-blocking).
            await Task.Delay(options.PollInterval, timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    private static SqliteException Failure(sqlite3 handle, int rc, int attempt, int autocommitAtEntry)
    {
        // Capture the full diagnosis straight off the handle. The bare exec return code plus errmsg can
        // read as the meaningless "SQLite Error 1: 'not an error'" when a pooled handle's error slot and
        // the returned rc disagree (e.g. a BEGIN issued on a handle already inside a transaction); the
        // extended errcode and the autocommit flag make that state attributable instead of a dead end.
        //
        // The attempt index and the entry autocommit are here because #578 could not be read off the
        // trace: the throw site is identical on the first pass and the hundredth, so "the handle already
        // had no transaction" and "this attempt lost the one it had" produced the same report. Both
        // facts are cheap and only ever read on the failure path.
        var errcode = raw.sqlite3_errcode(handle);
        var extended = raw.sqlite3_extended_errcode(handle);
        var autocommit = raw.sqlite3_get_autocommit(handle);
        var message = raw.sqlite3_errmsg(handle).utf8_to_string();

        // Keep the primary result code (the exec return's low byte) as SqliteErrorCode — the same value
        // callers see today — and carry the extended code through SqliteExtendedErrorCode.
        return new SqliteException(
            $"SQLite Error {rc} (errcode {errcode}, extended {extended}, autocommit {autocommit}, " +
            $"autocommit on entry {autocommitAtEntry}, attempt {attempt}): '{message}'.",
            rc & 0xFF,
            extended);
    }
}
