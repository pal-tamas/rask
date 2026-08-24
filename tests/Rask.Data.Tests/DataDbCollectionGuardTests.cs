using Rask.Tests.Shared;

namespace Rask.Data.Tests;

// In the collection it guards, so it costs the suite nothing and needs no exemption of its own.
[Collection(DataDbCollection.Name)]
public sealed class DataDbCollectionGuardTests
{
    [Fact]
    public void Every_test_class_is_collected_or_named_as_one_that_never_builds_a_context() =>
        DbCollectionGuard.AssertEveryTestClassIsCollected(
            typeof(DataDbCollectionGuardTests).Assembly,
            DataDbCollection.Name);
}
