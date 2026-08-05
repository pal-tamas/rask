using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;

namespace Rask.Jobs.Tests;

/// <summary>
/// The jobs pillar's metrics. What matters is that the numbers are real (they come from the processor
/// actually running work, not from a unit poking the meter) and that a listener is what makes the
/// queue-depth gauges cost anything at all.
/// </summary>
public sealed class JobMetricsTests
{
    [Fact]
    public async Task Running_a_job_records_a_processed_count_and_a_duration()
    {
        await using var h = new JobsHarness();
        using var collector = new MetricCollector(h.Get<JobMetrics>());

        await h.Queue.EnqueueAsync(new RecordJob("metered"));
        await h.RunUntilAsync(() => collector.Sum("rask.jobs.processed") >= 1);

        Assert.Equal(1, collector.Sum("rask.jobs.processed"));
        Assert.Equal("Rask.Jobs.Tests.RecordJob", collector.LastTag("rask.jobs.processed", "job.type"));

        // The histogram fired for the same job. Duration is wall-clock, so assert it was recorded rather
        // than pinning a value.
        Assert.True(collector.Count("rask.jobs.duration") >= 1);
    }

    [Fact]
    public async Task A_failing_job_counts_every_attempt_and_dead_letters_once()
    {
        // MaxAttempts: 2 with no backoff, so both attempts happen inside the test rather than an hour apart.
        await using var h = new JobsHarness(o =>
        {
            o.MaxAttempts = 2;
            o.BaseRetryDelay = TimeSpan.Zero;
            o.MaxRetryDelay = TimeSpan.Zero;
        });
        using var collector = new MetricCollector(h.Get<JobMetrics>());

        await h.Queue.EnqueueAsync(new FailingJob());
        await h.RunUntilAsync(() => collector.Sum("rask.jobs.deadlettered") >= 1);

        // Every attempt is a failure; only the attempt that exhausts MaxAttempts is a dead letter. Counting
        // dead letters per attempt would make "how many jobs have given up" unanswerable.
        Assert.Equal(2, collector.Sum("rask.jobs.failed"));
        Assert.Equal(1, collector.Sum("rask.jobs.deadlettered"));
        Assert.Equal(0, collector.Sum("rask.jobs.processed"));
    }

    [Fact]
    public async Task An_unregistered_job_type_counts_failures_and_then_a_dead_letter()
    {
        // A renamed job that nobody re-registered is the most ordinary way a production queue starts
        // abandoning work. It never reaches a handler, so it has to be counted on the deserialize path.
        await using var h = new JobsHarness(o =>
        {
            o.MaxAttempts = 2;
            o.BaseRetryDelay = TimeSpan.Zero;
            o.MaxRetryDelay = TimeSpan.Zero;
        });
        using var collector = new MetricCollector(h.Get<JobMetrics>());

        await using (var db = h.NewContext())
        {
            var now = h.Clock.GetUtcNow().UtcDateTime;
            db.Set<Job>().Add(new Job
            {
                Type = "Nothing.Registered.Here",
                Payload = "{}",
                RunAt = now,
                CreatedAt = now,
            });
            await db.SaveChangesAsync();
        }

        await h.RunUntilAsync(() => collector.Sum("rask.jobs.deadlettered") >= 1);

        Assert.Equal(2, collector.Sum("rask.jobs.failed"));
        Assert.Equal(1, collector.Sum("rask.jobs.deadlettered"));
    }

    [Fact]
    public async Task The_queue_depth_gauges_report_pending_and_dead_lettered_separately()
    {
        await using var h = new JobsHarness(o =>
        {
            o.MaxAttempts = 1;
            o.BaseRetryDelay = TimeSpan.Zero;
            o.MaxRetryDelay = TimeSpan.Zero;
        });
        using var collector = new MetricCollector(h.Get<JobMetrics>());

        await h.Queue.EnqueueAsync(new FailingJob());                          // becomes a dead letter
        await h.Queue.ScheduleAsync(new RecordJob("later"), TimeSpan.FromHours(1));   // stays pending

        await h.RunUntilAsync(() =>
        {
            collector.CollectObservable();
            return collector.LastValue("rask.jobs.deadletters") == 1;
        });

        collector.CollectObservable();
        Assert.Equal(1, collector.LastValue("rask.jobs.deadletters"));
        Assert.Equal(1, collector.LastValue("rask.jobs.pending"));
    }

    [Fact]
    public async Task Nothing_is_sampled_while_no_one_is_listening()
    {
        // The whole reason the sample lives on the poll instead of in the gauge callback: an app that
        // exports no metrics must not pay two COUNT(*) queries every five seconds, forever.
        await using var h = new JobsHarness();
        var metrics = h.Get<JobMetrics>();

        Assert.False(metrics.WantsQueueDepth);

        using var collector = new MetricCollector(metrics);
        Assert.True(metrics.WantsQueueDepth);
    }

    /// <summary>Collects from one <see cref="JobMetrics"/> instance, ignoring any other meter in the process.</summary>
    private sealed class MetricCollector : IDisposable
    {
        private readonly MeterListener _listener;
        private readonly Lock _gate = new();
        private readonly Dictionary<string, long> _sums = [];
        private readonly Dictionary<string, int> _counts = [];
        private readonly Dictionary<string, int> _lastValue = [];
        private readonly Dictionary<string, string?> _lastTag = [];

        public MetricCollector(JobMetrics metrics)
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

            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                lock (_gate)
                {
                    _sums[instrument.Name] = _sums.GetValueOrDefault(instrument.Name) + value;
                    _counts[instrument.Name] = _counts.GetValueOrDefault(instrument.Name) + 1;
                    RecordTags(instrument.Name, tags);
                }
            });

            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            {
                lock (_gate)
                {
                    _counts[instrument.Name] = _counts.GetValueOrDefault(instrument.Name) + 1;
                    RecordTags(instrument.Name, tags);
                }
            });

            _listener.SetMeasurementEventCallback<int>((instrument, value, _, _) =>
            {
                lock (_gate)
                {
                    _lastValue[instrument.Name] = value;
                }
            });

            _listener.Start();
        }

        public long Sum(string instrument)
        {
            lock (_gate) { return _sums.GetValueOrDefault(instrument); }
        }

        public int Count(string instrument)
        {
            lock (_gate) { return _counts.GetValueOrDefault(instrument); }
        }

        public int LastValue(string instrument)
        {
            lock (_gate) { return _lastValue.GetValueOrDefault(instrument, -1); }
        }

        public string? LastTag(string instrument, string tag)
        {
            lock (_gate) { return _lastTag.GetValueOrDefault($"{instrument}/{tag}"); }
        }

        public void CollectObservable() => _listener.RecordObservableInstruments();

        public void Dispose() => _listener.Dispose();

        private void RecordTags(string instrument, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            foreach (var tag in tags)
            {
                _lastTag[$"{instrument}/{tag.Key}"] = tag.Value?.ToString();
            }
        }
    }
}
