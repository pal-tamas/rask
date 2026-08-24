using Rask.Tests.Shared;

namespace Rask.Outbox.Tests;

// In the collection it guards, so it costs the suite nothing and needs no exemption of its own.
[Collection(OutboxDbCollection.Name)]
public sealed class OutboxDbCollectionGuardTests
{
    [Fact]
    public void Every_test_class_is_collected_or_named_as_one_that_never_builds_a_context() =>
        DbCollectionGuard.AssertEveryTestClassIsCollected(
            typeof(OutboxDbCollectionGuardTests).Assembly,
            OutboxDbCollection.Name,
            // The process-global serializer registry, driven through per-test group keys. It rebuilds
            // its lookup under a lock and installs it in a single volatile store, so a reader observes
            // either the whole old map or the whole new one — concurrent use is the design, not a race.
            "OutboxSerializerRegistryTests",
            "OutboxSerializerRegistryReplaceTests");
}
