namespace Rask.Data.Tests;

// In the collection it guards, so it costs the suite nothing and needs no exemption of its own.
[Collection(DataDbCollection.Name)]
public sealed class DataDbCollectionGuardTests
{
    [Fact]
    public void Every_test_class_is_collected_or_named_as_one_that_never_builds_a_context() =>
        DbCollectionGuard.AssertEveryTestClassIsCollected(
            typeof(DataDbCollectionGuardTests).Assembly,
            DataDbCollection.Name,
            // Ambient values over a stub scope; it never builds a context.
            nameof(CurrentTests),
            // Reflection over annotations only; it never builds a context.
            nameof(TrimmingContractTests),
            // Index builders over a bare ModelBuilder; it never builds a context.
            nameof(UniqueWithAMessageTests),
            // Lists of failures built from strings; it never builds a context.
            nameof(GeneratedModelRulesTests),
            // Value objects compared in memory; it never builds a context.
            nameof(ValueObjectEqualityTests),
            // Value objects made in memory, their rules asked; it never builds a context.
            nameof(BaseRecordValueObjectRuleTests),
            // Collation names read into cultures; it never builds a context.
            nameof(TextCollationTests),
            // SQL text read into a record; it never builds a context.
            nameof(BulkDeleteTests),
            // Refusals thrown from conditions and lambdas; it never builds a context.
            nameof(RefuseTests),
            // A generated model checked by the rules it borrows, in memory; it never builds a context.
            nameof(FluentModelFormTests),
            // A wall-clock time turned into UTC and back; it never builds a context.
            nameof(WallClockInUtcTests),
            // Builds a context type of its own, in a collection that runs alone after every parallel one.
            nameof(AppModelRulesTests),
            // The same, under the rules that name the app's tenant.
            nameof(TenantReferencesReadModelTests));

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
