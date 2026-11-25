using Rask.Tests.Shared;

namespace Rask.Storage.Tests;

/// <summary>
///     Every storage test runs on a SQLite database in memory, its own, so the suite's classes run side by side; this
///     keeps a database file from coming back in a test copied from an old one. The stored files themselves stay on
///     disk, in a directory of each test's own.
/// </summary>
public sealed class SqliteFileGuardTests
{
    [Fact]
    public void No_test_puts_its_database_in_a_file() =>
        SqliteFileGuard.AssertNoTestOpensASqliteFile([
            // "Seen only once it commits": a second connection reads while the work holds its write open, which a
            // file's locks allow and a shared in-memory database's table lock refuses.
            "StorageDbTransactionTests.cs",
            // A foreign object named like a backup in the storage directory, which the sweep must leave alone: a file
            // of one byte, never opened as a database.
            "OrphanSweeperTests.cs",
            // A bucket object's key, named like a SQLite file and signed for a ranged GET: no database is opened.
            "S3Tests.cs",
        ]);
}
