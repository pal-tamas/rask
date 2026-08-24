using Rask.Tests.Shared;

namespace Rask.Jobs.Tests;

// In the collection it guards, so it costs the suite nothing and needs no exemption of its own.
[Collection(JobsDbCollection.Name)]
public sealed class JobsDbCollectionGuardTests
{
    [Fact]
    public void Every_test_class_is_collected_or_named_as_one_that_never_builds_a_context() =>
        DbCollectionGuard.AssertEveryTestClassIsCollected(
            typeof(JobsDbCollectionGuardTests).Assembly,
            JobsDbCollection.Name,
            // Pure unit tests: options validation and the process-global serializer registry, which
            // takes a lock and installs its lookup in a single store, and is driven here through
            // per-test group keys — so there is no context and nothing to serialise.
            "JobOptionsTests",
            "JobSerializerRegistryTests",
            "JobSerializerRegistryReplaceTests");
}
