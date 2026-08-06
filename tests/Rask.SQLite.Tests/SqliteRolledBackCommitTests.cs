using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace Rask.SQLite.Tests;

// #578: a COMMIT answered with SQLITE_BUSY may find its transaction already gone, because SQLite rolls a
// contended transaction back on its own. The retry used to sleep and re-issue COMMIT into autocommit,
// which fails with the non-retryable "cannot commit - no transaction is active" — the commit-side twin of
// the BEGIN bug fixed in #504. These tests pin the three decisions that replaced it: recognise the
// rollback instead of retrying the COMMIT, re-run the whole transaction because the work is gone, and
// refuse to re-run when the transaction ended somewhere the outcome is ambiguous.
public sealed class SqliteRolledBackCommitTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-sqlite-rb-{Guid.NewGuid():N}.db");
    private readonly string _connectionString;

    public SqliteRolledBackCommitTests()
    {
        _connectionString = $"Data Source={_dbPath}";
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        // Rollback-journal mode, not WAL: an EXCLUSIVE lock held by another connection is the one way to
        // make a real SQLite build answer SQLITE_BUSY on demand, with no timing window to lose.
        Exec(connection, "PRAGMA journal_mode=DELETE;");
        Exec(connection, "CREATE TABLE t(id INTEGER PRIMARY KEY, v TEXT NOT NULL);");
    }

    [Fact]
    public async Task A_contended_statement_that_lost_its_transaction_is_reported_as_a_rollback()
    {
        // The decision under test in isolation: the statement was answered with a contended lock and no
        // transaction is left on the handle. Retrying it can only produce "no transaction is active", so
        // the loop must stop and say so. A blocked BEGIN is the vehicle — it is the only statement a test
        // can make SQLite answer BUSY deterministically — but the branch it exercises is the one COMMIT
        // takes in production.
        await using var blocker = await OpenAsync();
        Exec(blocker, "BEGIN EXCLUSIVE;");

        await using var contended = await OpenAsync();
        var handle = contended.Handle!;
        raw.sqlite3_busy_timeout(handle, 0);
        Assert.Equal(1, raw.sqlite3_get_autocommit(handle));

        var rolledBack = await Assert.ThrowsAsync<SqliteTransactionRolledBackException>(() =>
            SqliteBusyRetry.ExecAsync(
                handle,
                "BEGIN IMMEDIATE;",
                new SqliteBusyRetryOptions { Timeout = TimeSpan.FromSeconds(5) },
                TimeProvider.System,
                CancellationToken.None,
                requiresTransaction: true));

        Assert.Equal("BEGIN IMMEDIATE;", rolledBack.Sql);
        Assert.Equal(1, rolledBack.Attempt);
    }

    [Fact]
    public async Task A_contended_statement_that_still_has_its_transaction_keeps_retrying()
    {
        // The negative control for the check above: same contended lock, but the transaction is still
        // open, so this is the ordinary busy wait that must not be mistaken for a rollback. It retries to
        // the timeout and surfaces SQLITE_BUSY exactly as before.
        await using var blocker = await OpenAsync();
        Exec(blocker, "BEGIN EXCLUSIVE;");

        await using var contended = await OpenAsync();
        var handle = contended.Handle!;
        raw.sqlite3_busy_timeout(handle, 0);

        // A deferred BEGIN takes no lock, so the transaction is open while the write below is refused.
        Exec(contended, "BEGIN;");
        Assert.Equal(0, raw.sqlite3_get_autocommit(handle));

        var exception = await Assert.ThrowsAsync<SqliteException>(() =>
            SqliteBusyRetry.ExecAsync(
                handle,
                "INSERT INTO t(v) VALUES('blocked');",
                new SqliteBusyRetryOptions { Timeout = TimeSpan.FromMilliseconds(50) },
                TimeProvider.System,
                CancellationToken.None,
                requiresTransaction: true));

        Assert.Equal(raw.SQLITE_BUSY, exception.SqliteErrorCode);
        Exec(contended, "ROLLBACK;");
    }

    [Fact]
    public async Task The_diagnosis_names_the_attempt_and_the_transaction_state_it_started_from()
    {
        // #578 could not be read off its own stack trace: the throw site is identical on the first pass
        // and the hundredth, so "arrived without a transaction" and "lost one while running" produced the
        // same report. Both facts are now in the message.
        await using var blocker = await OpenAsync();
        Exec(blocker, "BEGIN EXCLUSIVE;");

        await using var contended = await OpenAsync();
        var handle = contended.Handle!;
        raw.sqlite3_busy_timeout(handle, 0);

        var exception = await Assert.ThrowsAsync<SqliteException>(() =>
            SqliteBusyRetry.ExecAsync(
                handle,
                "INSERT INTO t(v) VALUES('blocked');",
                new SqliteBusyRetryOptions { Timeout = TimeSpan.FromMilliseconds(20) },
                TimeProvider.System,
                CancellationToken.None));

        Assert.Contains("autocommit on entry 1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("attempt ", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rolled_back_transaction_is_run_again_from_the_beginning()
    {
        // The recovery itself: everything the work delegate wrote is gone with the transaction, so the
        // transaction is re-run rather than the COMMIT retried. The delegate raises the package's own
        // rollback signal on its first pass — the same signal a contended COMMIT raises, injected at the
        // one boundary a test can reach it from, since no SQLite build rolls a commit back on request.
        await using var connection = await OpenAsync();

        var invocations = 0;
        await connection.ExecuteInImmediateTransactionAsync(
            new SqliteBusyRetryOptions { Timeout = TimeSpan.FromSeconds(5) },
            async (c, ct) =>
            {
                invocations++;
                await using var command = c.CreateCommand();
                command.CommandText = "INSERT INTO t(v) VALUES('retried');";
                await command.ExecuteNonQueryAsync(ct);

                if (invocations == 1)
                {
                    throw new SqliteTransactionRolledBackException("COMMIT;", 1);
                }
            });

        Assert.Equal(2, invocations);

        // Exactly one row: the first attempt's insert went with the transaction it was rolled back with,
        // and the second attempt started from a clean BEGIN rather than inheriting it.
        Assert.Equal(1, Count());
        Assert.Equal(1, raw.sqlite3_get_autocommit(connection.Handle!));
    }

    [Fact]
    public async Task Re_running_stops_at_the_configured_timeout_and_says_what_was_lost()
    {
        // The budget is the caller's Timeout measured from entry, not per attempt — otherwise a write
        // path configured for 5 seconds could stall for as long as SQLite kept rolling it back.
        await using var connection = await OpenAsync();

        var invocations = 0;
        var exception = await Assert.ThrowsAsync<SqliteException>(() =>
            connection.ExecuteInImmediateTransactionAsync(
                new SqliteBusyRetryOptions { Timeout = TimeSpan.FromMilliseconds(50) },
                (_, _) =>
                {
                    invocations++;
                    throw new SqliteTransactionRolledBackException("COMMIT;", 1);
                }));

        Assert.True(invocations > 1, $"expected more than one attempt, got {invocations}");

        // SQLITE_ABORT / SQLITE_ABORT_ROLLBACK, SQLite's own "the transaction was rolled back" — not the
        // SQLITE_ERROR(1) "cannot commit - no transaction is active" the doomed retry used to report.
        Assert.Equal(raw.SQLITE_ABORT, exception.SqliteErrorCode);
        Assert.Equal(516, exception.SqliteExtendedErrorCode);
        Assert.Contains("rolled the transaction back", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("no transaction is active", exception.Message, StringComparison.Ordinal);

        // Nothing survived, and the handle is clean enough to go back to the pool.
        Assert.Equal(0, Count());
        Assert.Equal(1, raw.sqlite3_get_autocommit(connection.Handle!));
    }

    [Fact]
    public async Task A_transaction_that_ends_inside_the_work_delegate_is_not_re_run()
    {
        // The boundary of the retry. When the transaction ends while the delegate is running, its writes
        // may have been committed rather than lost — SQLite can roll a contended statement back and
        // Microsoft.Data.Sqlite's own retry then re-runs it in autocommit. Re-running the delegate could
        // duplicate durable rows, so this case is surfaced instead.
        await using var connection = await OpenAsync();

        var invocations = 0;
        var exception = await Assert.ThrowsAsync<SqliteException>(() =>
            connection.ExecuteInImmediateTransactionAsync(
                new SqliteBusyRetryOptions { Timeout = TimeSpan.FromSeconds(5) },
                async (c, ct) =>
                {
                    invocations++;
                    await using var command = c.CreateCommand();
                    command.CommandText = "INSERT INTO t(v) VALUES('self-committed'); COMMIT;";
                    await command.ExecuteNonQueryAsync(ct);
                }));

        Assert.Equal(1, invocations);
        Assert.Equal(raw.SQLITE_ABORT, exception.SqliteErrorCode);
        Assert.Contains("cannot be told apart", exception.Message, StringComparison.Ordinal);

        // The row the delegate committed for itself is still there — which is exactly why re-running the
        // delegate would have written a second one.
        Assert.Equal(1, Count());
    }

    [Fact]
    public async Task A_cancelled_transaction_still_rolls_back_before_the_handle_goes_back_to_the_pool()
    {
        // Teardown used to take the caller's token, and the retry checks for cancellation before its
        // first attempt — so a write cancelled mid-transaction skipped the rollback entirely and handed a
        // mid-transaction handle to the pool. Only the next ExecuteInImmediateTransactionAsync lease
        // cleared it; a plain query, EF, or the pragma batch on reopen inherited the open transaction.
        await using var connection = await OpenAsync();
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            connection.ExecuteInImmediateTransactionAsync(
                new SqliteBusyRetryOptions(),
                async (c, ct) =>
                {
                    await using var command = c.CreateCommand();
                    command.CommandText = "INSERT INTO t(v) VALUES('cancelled');";
                    await command.ExecuteNonQueryAsync(ct);

                    await cts.CancelAsync();
                    cts.Token.ThrowIfCancellationRequested();
                },
                cancellationToken: cts.Token));

        // The handle is back in autocommit, so the next lease of it starts clean, and the write went with
        // the transaction it was rolled back with.
        Assert.Equal(1, raw.sqlite3_get_autocommit(connection.Handle!));
        Assert.Equal(0, Count());
    }

    private async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static void Exec(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private long Count()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM t;";
        return (long)command.ExecuteScalar()!;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, $"{_dbPath}-shm", $"{_dbPath}-wal", $"{_dbPath}-journal" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
