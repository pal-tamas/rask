using Rask.Tests.Shared;

namespace Rask.Logging.Tests;

public sealed class SqliteFileGuardTests
{
    [Fact]
    public void No_test_puts_its_log_store_in_a_file_unless_the_test_is_about_the_file() =>
        SqliteFileGuard.AssertNoTestOpensASqliteFile([
            // What a restarted process finds: the entries outlive the store, and the process, that wrote them.
            "LogStoreRestartTests.cs",
        ]);
}
