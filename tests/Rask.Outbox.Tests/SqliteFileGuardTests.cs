using Rask.Tests.Shared;

namespace Rask.Outbox.Tests;

/// <summary>
///     Every outbox test runs on a SQLite database in memory, its own, so the suite's classes run side by side; this
///     keeps a database file from coming back in a test copied from an old one.
/// </summary>
public sealed class SqliteFileGuardTests
{
    [Fact]
    public void No_test_puts_its_database_in_a_file() =>
        // None of the outbox tests is about a database file, so none is allowed one.
        SqliteFileGuard.AssertNoTestOpensASqliteFile([]);
}
