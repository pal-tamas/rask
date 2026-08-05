using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace Rask.SQLite;

/// <summary>
/// Transaction helpers that complete the SQLite concurrency story on top of the production pragmas:
/// starting write transactions with <c>BEGIN IMMEDIATE</c>, and a non-blocking, fair-interval busy-retry
/// for the contended write lock.
/// </summary>
public static class SqliteConnectionExtensions
{
    /// <summary>
    /// Begins a transaction with <c>BEGIN IMMEDIATE</c> (takes the write lock up front) instead of the
    /// default deferred <c>BEGIN</c>. Under concurrency a deferred read-then-write transaction can
    /// dead-lock two upgrading readers into an <b>unretryable</b> <c>SQLITE_BUSY</c> (SQLite never even
    /// invokes the busy handler); <c>BEGIN IMMEDIATE</c> turns that into a plain, waitable lock wait.
    /// Prefer this for any transaction that reads and then writes.
    /// </summary>
    /// <remarks>
    /// This is a synchronous begin: while it waits for the write lock it blocks the calling thread inside
    /// Microsoft.Data.Sqlite. For a genuinely non-blocking write use
    /// <see cref="ExecuteInImmediateTransactionAsync{T}"/>, which yields the thread while it waits.
    /// </remarks>
    public static SqliteTransaction BeginImmediate(this SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return connection.BeginTransaction(deferred: false);
    }

    /// <summary>
    /// Runs <paramref name="work"/> inside a <c>BEGIN IMMEDIATE</c> transaction, acquiring (and, if
    /// contended, committing) the write lock through a <b>non-blocking, fair-interval</b> retry that
    /// yields the calling thread between attempts — a genuinely non-blocking busy
    /// handler. Commits when <paramref name="work"/> returns; rolls back if it throws.
    /// </summary>
    /// <remarks>
    /// The lock is taken via the raw <c>sqlite3</c> handle with SQLite's native busy handler disabled, so
    /// the wait happens entirely in managed, thread-freeing code (a constant
    /// <see cref="SqliteBusyRetryOptions.PollInterval"/>, up to <see cref="SqliteBusyRetryOptions.Timeout"/>)
    /// rather than blocking a thread inside native code or Microsoft.Data.Sqlite's synchronous retry.
    /// Because the transaction is begun on the native handle, no <see cref="SqliteTransaction"/> object is
    /// exposed — issue your statements with <see cref="SqliteConnection.CreateCommand"/> inside
    /// <paramref name="work"/>; they run inside the transaction. The connection must already be open, and
    /// its <c>busy_timeout</c> is set to <c>0</c> as a side effect.
    /// </remarks>
    public static async Task<T> ExecuteInImmediateTransactionAsync<T>(
        this SqliteConnection connection,
        SqliteBusyRetryOptions retry,
        Func<SqliteConnection, CancellationToken, Task<T>> work,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(retry);
        ArgumentNullException.ThrowIfNull(work);
        retry.Validate();

        var time = timeProvider ?? TimeProvider.System;
        var handle = connection.Handle
            ?? throw new InvalidOperationException(
                "The connection must be open before starting an immediate transaction.");

        // Own all waiting in managed, thread-yielding code: disable SQLite's native busy handler so
        // sqlite3_exec returns SQLITE_BUSY immediately instead of sleeping the thread inside native code,
        // and drive BEGIN/COMMIT through the raw handle so Microsoft.Data.Sqlite's own synchronous
        // Thread.Sleep retry (governed by CommandTimeout) never runs either.
        raw.sqlite3_busy_timeout(handle, 0);

        try
        {
            // BEGIN/COMMIT/ROLLBACK run through the raw handle, bypassing Microsoft.Data.Sqlite's
            // SqliteTransaction bookkeeping, and the underlying sqlite3 handle is pooled and reused. If a
            // transaction is open on it, BEGIN IMMEDIATE hits "cannot start a transaction within a
            // transaction" (SQLITE_ERROR) — a non-retryable fast failure. requiresAutocommit clears any
            // such transaction before every attempt, so BEGIN always starts from autocommit.
            //
            // Before *every* attempt, not just the first, because the handle can enter a transaction
            // between passes as well as arrive in one: an earlier lease can leak one, and a BEGIN
            // answered with an extended SQLITE_BUSY (BUSY_SNAPSHOT / BUSY_RECOVERY, which the primary
            // result code hides) can leave one behind of its own. A guard that ran once, before the loop,
            // caught the first case and missed the second — the retry then re-issued BEGIN inside the
            // transaction its own previous attempt had opened, turning a contended lock into that
            // misleading non-retryable error. That is #504.
            //
            // The rollback is retried rather than fire-and-forget: it can itself return SQLITE_BUSY
            // ("cannot rollback transaction - SQL statements in progress") when a GC-orphaned reader's
            // statement is still active on the pooled handle — the same hazard docs/sqlite.md documents
            // for EF's pool return. The busy_timeout was set to 0 just now, so the native handler won't
            // wait for us either; this managed retry is the only thing that does.
            //
            // Inside the try: a BEGIN that fails partway must still go through the cleanup below, or the
            // handle goes back to the pool mid-transaction and poisons every later lease of it.
            await SqliteBusyRetry.ExecAsync(
                handle, "BEGIN IMMEDIATE;", retry, time, cancellationToken, requiresAutocommit: true).ConfigureAwait(false);
            var result = await work(connection, cancellationToken).ConfigureAwait(false);
            await SqliteBusyRetry.ExecAsync(handle, "COMMIT;", retry, time, cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            // Never hand a mid-transaction handle back to the pool. On the happy path COMMIT already
            // restored autocommit and this is a no-op; on any failure (a throwing work, or a COMMIT that
            // never completed) this rolls the open transaction back.
            //
            // Retried, for the same reason the entry rollback is: a single fire-and-forget exec here
            // returns a mid-transaction handle to the pool whenever the rollback meets a still-active
            // statement. It stays best-effort — a rollback that cannot run must not replace the
            // exception that brought us here, and the next lease's requiresAutocommit guard clears the
            // handle anyway — so every failure is swallowed. It honours the caller's token, so a
            // cancelled operation gives up immediately rather than stalling teardown.
            if (raw.sqlite3_get_autocommit(handle) == 0)
            {
                try
                {
                    await SqliteBusyRetry.ExecAsync(handle, "ROLLBACK;", retry, time, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (SqliteException)
                {
                    // The transaction may already be gone, or the handle may still be busy.
                }
                catch (OperationCanceledException)
                {
                    // Teardown under cancellation: leave it to the next lease's guard.
                }
            }
        }
    }

    /// <summary>
    /// The result-less overload of <see cref="ExecuteInImmediateTransactionAsync{T}"/>.
    /// </summary>
    public static Task ExecuteInImmediateTransactionAsync(
        this SqliteConnection connection,
        SqliteBusyRetryOptions retry,
        Func<SqliteConnection, CancellationToken, Task> work,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        return connection.ExecuteInImmediateTransactionAsync(
            retry,
            async (c, ct) =>
            {
                await work(c, ct).ConfigureAwait(false);
                return (object?)null;
            },
            timeProvider,
            cancellationToken);
    }
}
