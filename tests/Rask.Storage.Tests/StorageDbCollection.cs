using Rask.Tests.Shared;

namespace Rask.Storage.Tests;

/// <summary>
///     The classes that build a <see cref="StorageDbContext" />, run as one xUnit collection. EF Core's model cache
///     is per process and keyed on the context type, so per-class SQLite files still share one <c>IModel</c> —
///     see <c>Rask.Jobs.Tests.JobsDbCollection</c> for the race.
/// </summary>
[CollectionDefinition(Name)]
public sealed class StorageDbCollection
{
    public const string Name = "storage-db";
}

// In the collection it guards, so it costs the suite nothing and needs no exemption of its own.
[Collection(StorageDbCollection.Name)]
public sealed class StorageDbCollectionGuardTests
{
    [Fact]
    public void Every_test_class_is_collected_or_named_as_one_that_never_builds_a_context() =>
        DbCollectionGuard.AssertEveryTestClassIsCollected(
            typeof(StorageDbCollectionGuardTests).Assembly,
            StorageDbCollection.Name,
            // Pure functions and a bare directory: no EF anywhere in them.
            "ContentSnifferTests",
            "ContentTypePolicyTests",
            "SafeFileNameTests",
            "KeyLayoutTests",
            "TemporaryUrlProtectorTests",
            "DiskBlobBackendTests",
            "StorageOptionsTests");
}
