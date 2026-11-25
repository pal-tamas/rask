using Rask.Tests.Shared;

namespace Rask.Server.Tests.App;

public sealed class SqliteFileGuardTests
{
    [Fact]
    public void No_test_puts_its_database_in_a_file_unless_the_test_is_about_the_file() =>
        SqliteFileGuard.AssertNoTestOpensASqliteFile([
            // Migrate-on-start into a real file, and the proof that nothing opened it is that the file never appeared.
            "MigrateOnStartTests.cs",
            // The Litestream restore runs only for a database file that is not there yet; the app.db default is a
            // string the tests read back from configuration, never opened.
            "RaskDatabaseProviderTests.cs",
            // A connection string from code beats one from configuration: both strings are compared, never opened.
            "RaskConfigurationTests.cs",
            // A mapped app whose database file does not exist yet still starts.
            "RaskBatteryTests.cs",
        ]);
}
