using Rask.Tests.Shared;

namespace Rask.Mail.Tests;

// In the collection it guards, so it costs the suite nothing and needs no exemption of its own.
[Collection(MailDbCollection.Name)]
public sealed class MailDbCollectionGuardTests
{
    [Fact]
    public void Every_test_class_is_collected_or_named_as_one_that_never_builds_a_context() =>
        DbCollectionGuard.AssertEveryTestClassIsCollected(
            typeof(MailDbCollectionGuardTests).Assembly,
            MailDbCollection.Name,
            // Names MailDbContext only as the type argument to AddRaskMail while asserting option
            // validation; never builds a context.
            "MailUnitTests",
            // Reads the metrics counters the processor emits — no context of its own.
            "MailMetricsTests");
}
