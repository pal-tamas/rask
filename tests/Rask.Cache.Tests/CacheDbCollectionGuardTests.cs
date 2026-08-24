using Rask.Tests.Shared;

namespace Rask.Cache.Tests;

// In the collection it guards, so it costs the suite nothing and needs no exemption of its own.
[Collection(CacheDbCollection.Name)]
public sealed class CacheDbCollectionGuardTests
{
    [Fact]
    public void Every_test_class_is_collected_or_named_as_one_that_never_builds_a_context() =>
        DbCollectionGuard.AssertEveryTestClassIsCollected(
            typeof(CacheDbCollectionGuardTests).Assembly,
            CacheDbCollection.Name,
            // Names CacheDbContext only as the type argument to AddRaskCache while asserting option
            // validation; never builds a context, so it never touches the model.
            "CacheOptionsTests",
            // Drives an in-memory external store through the cache abstraction — no EF at all.
            "ExternalStoreCacheTests");
}
