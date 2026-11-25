namespace Rask.SQLite.Tests;

public sealed class SqliteFileGuardTests
{
    [Fact]
    public void Only_a_test_about_the_file_puts_its_database_in_one() =>
        SqliteFileGuard.AssertNoTestOpensASqliteFile(
        [
            // Asserts journal_mode=WAL, which only a file database can be in.
            "RaskSqliteIntegrationTests.cs",
            // Rows that survive a fresh connection, as they survive an app restart.
            "RaskSqlitePersistenceTests.cs",
            // The factory creating the directory a database file lives in.
            "SqliteDatabaseDirectoryTests.cs",
            // Hundreds of writers contending for a WAL file's single write lock (SQLITE_BUSY, not shared-cache LOCKED).
            "SqliteConcurrencyStressTests.cs",
            // BEGIN IMMEDIATE holding a WAL file's write lock, waited out or timed out with SQLITE_BUSY.
            "SqliteImmediateTransactionTests.cs",
            // A rollback-journal file's EXCLUSIVE lock, the one way to make SQLite answer SQLITE_BUSY on demand.
            "SqliteRolledBackCommitTests.cs",
        ]);
}
