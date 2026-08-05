using System.Diagnostics.Metrics;

namespace Rask.Mail.Tests;

/// <summary>
/// The mail pillar's metrics. The shape assertion that matters here is the <b>absence</b> of a per-message
/// tag: subject and recipient are unbounded, and tagging by either would mint a time series per email sent.
/// </summary>
public sealed class MailMetricsTests
{
    [Fact]
    public void Counters_carry_no_per_message_tags()
    {
        using var metrics = new MailMetrics();
        using var collector = new TagCollector(metrics);

        metrics.Sent(12.5);
        metrics.Failed();
        metrics.DeadLettered();

        // Any tag at all on these would be a cardinality bug: there is no bounded dimension to use.
        Assert.Empty(collector.TagsFor("rask.mail.sent"));
        Assert.Empty(collector.TagsFor("rask.mail.failed"));
        Assert.Empty(collector.TagsFor("rask.mail.deadlettered"));
    }

    [Fact]
    public void Sent_records_both_the_count_and_the_duration()
    {
        using var metrics = new MailMetrics();
        using var collector = new TagCollector(metrics);

        metrics.Sent(42);

        Assert.Equal(1, collector.Count("rask.mail.sent"));
        Assert.Equal(1, collector.Count("rask.mail.duration"));
    }

    [Fact]
    public void Queue_depth_is_not_wanted_until_someone_subscribes()
    {
        using var metrics = new MailMetrics();

        // No listener: the processor must not pay for the two COUNT(*) queries.
        Assert.False(metrics.WantsQueueDepth);

        using var collector = new TagCollector(metrics);
        Assert.True(metrics.WantsQueueDepth);
    }

    [Fact]
    public void The_gauges_report_the_last_sample()
    {
        using var metrics = new MailMetrics();
        using var collector = new TagCollector(metrics);

        metrics.ObserveQueueDepth(pending: 7, deadLetters: 3);
        collector.CollectObservable();

        Assert.Equal(7, collector.LastValue("rask.mail.pending"));
        Assert.Equal(3, collector.LastValue("rask.mail.deadletters"));
    }

    private sealed class TagCollector : IDisposable
    {
        private readonly MeterListener _listener;
        private readonly Lock _gate = new();
        private readonly Dictionary<string, int> _counts = [];
        private readonly Dictionary<string, int> _lastValue = [];
        private readonly Dictionary<string, List<string>> _tags = [];

        public TagCollector(MailMetrics metrics)
        {
            var meter = metrics.Meter;
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (ReferenceEquals(instrument.Meter, meter))
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                },
            };

            _listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => Record(instrument.Name, tags));
            _listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) => Record(instrument.Name, tags));
            _listener.SetMeasurementEventCallback<int>((instrument, value, _, _) =>
            {
                lock (_gate) { _lastValue[instrument.Name] = value; }
            });

            _listener.Start();
        }

        public int Count(string instrument)
        {
            lock (_gate) { return _counts.GetValueOrDefault(instrument); }
        }

        public int LastValue(string instrument)
        {
            lock (_gate) { return _lastValue.GetValueOrDefault(instrument, -1); }
        }

        public IReadOnlyList<string> TagsFor(string instrument)
        {
            lock (_gate) { return _tags.GetValueOrDefault(instrument) ?? []; }
        }

        public void CollectObservable() => _listener.RecordObservableInstruments();

        public void Dispose() => _listener.Dispose();

        private void Record(string instrument, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            lock (_gate)
            {
                _counts[instrument] = _counts.GetValueOrDefault(instrument) + 1;
                var names = _tags.TryGetValue(instrument, out var existing) ? existing : _tags[instrument] = [];
                foreach (var tag in tags)
                {
                    names.Add(tag.Key);
                }
            }
        }
    }
}
