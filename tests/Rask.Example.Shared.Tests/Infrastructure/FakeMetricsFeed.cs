using Rask.Example.Shared.Demos;

namespace Rask.Example.Shared.Tests.Infrastructure;

// Inert IMetricsFeed test double — no background loop, so it's deterministic and starts
// no timers. State is settable and Publish() raises Updated, letting subscriber tests
// drive the producer by hand. Used both as the default registration in TestServices
// (page-baseline renders) and directly in MetricsViewTests.
internal sealed class FakeMetricsFeed : IMetricsFeed
{
    public FakeMetricsFeed(MetricsSnapshot? initial = null) =>
        State = initial ?? MetricsFeed.CreateInitialSnapshot(DateTimeOffset.UnixEpoch);

    public MetricsSnapshot State { get; private set; }

    public event Action? Updated;

    // Swap the snapshot, then notify — mirrors MetricsFeed's publish-then-raise order.
    public void Publish(MetricsSnapshot snapshot)
    {
        State = snapshot;
        Updated?.Invoke();
    }

    // Number of currently attached handlers — lets a test assert unsubscribe happened.
    public int SubscriberCount => Updated?.GetInvocationList().Length ?? 0;
}
