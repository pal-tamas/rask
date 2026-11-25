using Rask.Tests.Shared;

namespace Rask.Dashboard.Tests;

public sealed class SqliteFileGuardTests
{
    [Fact]
    public void No_test_puts_its_database_in_a_file() =>
        SqliteFileGuard.AssertNoTestOpensASqliteFile([]);
}
