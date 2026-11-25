namespace Rask.Data.Tests;

public sealed class SqliteFileGuardTests
{
    [Fact]
    public void No_test_puts_its_database_in_a_file_unless_the_file_is_what_it_tests() =>
        SqliteFileGuard.AssertNoTestOpensASqliteFile(
        [
            // TestDatabase.FromEnvironment makes a database per test BESIDE the named file, and drops it: the files are the subject.
            "TestDatabaseFromEnvironmentTests.cs",
            // A read from outside an open Db.Transaction must see the committed rows; a database in memory shares one
            // cache, whose table locks refuse that read (SQLITE_LOCKED) instead of answering it as a file's reader does.
            "DbTransactionTests.cs",
            "DbTransactionSpawnedWorkTests.cs",
        ]);
}
