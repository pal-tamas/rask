using Rask.Tests.Shared;

namespace Rask.WebPush.Tests;

/// <summary>
///     Every database here is in memory and the test's own, which is what lets the classes run side by side. This
///     fires when a database in a file comes back, as a test copied from an old one would bring it.
/// </summary>
public sealed class SqliteFileGuardTests
{
    [Fact]
    public void No_test_puts_its_database_in_a_file() =>
        SqliteFileGuard.AssertNoTestOpensASqliteFile([]);
}
