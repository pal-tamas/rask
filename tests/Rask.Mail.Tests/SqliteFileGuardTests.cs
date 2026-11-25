using Rask.Tests.Shared;

namespace Rask.Mailing.Tests;

/// <summary>
///     Every database here is in memory and the test's own, which is what lets the classes run side by side. This
///     fires when a database in a file comes back, as a test copied from an old one would bring it.
/// </summary>
public sealed class SqliteFileGuardTests
{
    [Fact]
    public void No_test_puts_its_database_in_a_file() =>
        SqliteFileGuard.AssertNoTestOpensASqliteFile(
        [
            // Asks, from a second connection, that a mail queued inside an open transaction is not seen before the
            // commit. A shared-cache in-memory database answers that with a table lock, not the committed state.
            "MailDbTransactionTests.cs",
        ]);
}
