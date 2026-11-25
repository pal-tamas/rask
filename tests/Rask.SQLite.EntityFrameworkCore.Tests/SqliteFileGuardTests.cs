namespace Rask.SQLite.EntityFrameworkCore.Tests;

public sealed class SqliteFileGuardTests
{
    [Fact]
    public void Only_a_test_about_the_file_puts_its_database_in_one() =>
        SqliteFileGuard.AssertNoTestOpensASqliteFile(
        [
            // A SaveChanges retrying against a file's held write lock (SQLITE_BUSY), then cancelled.
            "RaskSqliteCancelledRetryTests.cs",
            // Asserts journal_mode=WAL on every open, which only a file database can be in.
            "RaskSqliteInterceptorTests.cs",
            // Opening, and running the pragma script, while another connection holds a WAL file's write lock.
            "RaskSqliteOpenUnderLockTests.cs",
            // The execution strategy riding out a file's contended write lock (SQLITE_BUSY, not shared-cache LOCKED).
            "RaskSqliteExecutionStrategyTests.cs",
            // Probes a file's write lock from a second connection to tell IMMEDIATE from DEFERRED.
            "RaskSqliteTransactionModeTests.cs",
        ]);
}
