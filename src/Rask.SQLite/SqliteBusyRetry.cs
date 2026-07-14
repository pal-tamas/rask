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

            // Only a contended lock is retryable; every other result code is a real error.
            if (rc != raw.SQLITE_BUSY && rc != raw.SQLITE_LOCKED)
            {
                throw Failure(handle, rc);
            }

            if (timeProvider.GetElapsedTime(startedAt) >= options.Timeout)
            {
                throw Failure(handle, rc);
            }

            // The thread is free here — this is the whole point (Rails releases the GVL at this step).
            await Task.Delay(options.PollInterval, timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    private static SqliteException Failure(sqlite3 handle, int rc)
    {
        var message = raw.sqlite3_errmsg(handle).utf8_to_string();
        return new SqliteException($"SQLite Error {rc}: '{message}'.", rc);
    }
}
