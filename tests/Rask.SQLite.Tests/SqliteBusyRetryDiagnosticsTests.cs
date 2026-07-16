using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace Rask.SQLite.Tests;

// The retry loop classifies a non-BUSY/LOCKED result code as a real error and throws. This proves the
// thrown SqliteException is diagnosable: it carries the extended result code and the actual SQLite error
// text, rather than the meaningless "SQLite Error 1: 'not an error'" the bare exec code + errmsg produced
// when a pooled handle's error slot and the returned code disagreed.
public sealed class SqliteBusyRetryDiagnosticsTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-sqlite-diag-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task A_genuine_error_throws_a_diagnosable_exception()
    {
        await using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();

        var exception = await Assert.ThrowsAsync<SqliteException>(() =>
            SqliteBusyRetry.ExecAsync(
                connection.Handle!,
                "THIS IS NOT VALID SQL;",
                new SqliteBusyRetryOptions(),
                TimeProvider.System,
                CancellationToken.None));

        // SQLITE_ERROR(1), surfaced with the real parser message and the extended code — not "not an error".
        Assert.Equal(1, exception.SqliteErrorCode);
        Assert.DoesNotContain("not an error", exception.Message);
        Assert.Contains("extended", exception.Message);
        Assert.Contains("errcode", exception.Message);
        Assert.Contains("syntax error", exception.Message);
    }

    [Fact]
    public async Task Ok_statements_do_not_throw()
    {
        await using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();

        // A well-formed statement returns SQLITE_OK and completes without a diagnosis.
        await SqliteBusyRetry.ExecAsync(
            connection.Handle!,
            "CREATE TABLE IF NOT EXISTS ok(id INTEGER PRIMARY KEY);",
            new SqliteBusyRetryOptions(),
            TimeProvider.System,
            CancellationToken.None);

        Assert.Equal(1, raw.sqlite3_get_autocommit(connection.Handle!));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, $"{_dbPath}-shm", $"{_dbPath}-wal" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
