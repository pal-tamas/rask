using Rask.Tests.Shared;

namespace Rask.Background.Tests;

/// <summary>
///     Every jobs test runs on a SQLite database in memory, its own, so the suite's classes run side by side; this keeps
///     a database file from coming back in a test copied from an old one.
/// </summary>
public sealed class SqliteFileGuardTests
{
    [Fact]
    public void No_test_puts_its_database_in_a_file() =>
        // Two instances over one database share an in-memory one by its connection string, and a restart keeps the
        // first harness's database open across it — so a file is left only where a test is about what one gives.
        SqliteFileGuard.AssertNoTestOpensASqliteFile([
            // "Not seen before the commit": a second connection reads while the first holds its write open, which a
            // file's locks allow and a shared in-memory database's table lock refuses.
            "JobDbTransactionTests.cs",
        ]);
}
