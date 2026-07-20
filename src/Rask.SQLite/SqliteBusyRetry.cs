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
    internal static async Task ExecAsync(
        sqlite3 handle,
        string sql,
        SqliteBusyRetryOptions options,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetTimestamp();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rc = raw.sqlite3_exec(handle, sql);
            if (rc == raw.SQLITE_OK)
            {
                return;
            }

            // Only a contended lock is retryable; every other result code is a real error. Compare the
            // primary result code (low byte) so an extended BUSY/LOCKED variant is still treated as a
            // contended lock rather than misclassified as fatal.
            var primary = rc & 0xFF;
            if (primary != raw.SQLITE_BUSY && primary != raw.SQLITE_LOCKED)
            {
                throw Failure(handle, rc);
            }

            if (timeProvider.GetElapsedTime(startedAt) >= options.Timeout)
            {
                throw Failure(handle, rc);
            }

            // The thread is free here — this is the whole point (the wait is genuinely non-blocking).
            await Task.Delay(options.PollInterval, timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    private static SqliteException Failure(sqlite3 handle, int rc)
    {
        // Capture the full diagnosis straight off the handle. The bare exec return code plus errmsg can
        // read as the meaningless "SQLite Error 1: 'not an error'" when a pooled handle's error slot and
        // the returned rc disagree (e.g. a BEGIN issued on a handle already inside a transaction); the
        // extended errcode and the autocommit flag make that state attributable instead of a dead end.
        var errcode = raw.sqlite3_errcode(handle);
        var extended = raw.sqlite3_extended_errcode(handle);
        var autocommit = raw.sqlite3_get_autocommit(handle);
        var message = raw.sqlite3_errmsg(handle).utf8_to_string();

        // Keep the primary result code (the exec return's low byte) as SqliteErrorCode — the same value
        // callers see today — and carry the extended code through SqliteExtendedErrorCode.
        return new SqliteException(
            $"SQLite Error {rc} (errcode {errcode}, extended {extended}, autocommit {autocommit}): '{message}'.",
            rc & 0xFF,
            extended);
    }
}
