using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace Rask.Logging.Tests;

/// <summary>
/// The metrics, and above all <c>rask.logs.dropped</c> — the only thing in the system that will tell an
/// operator the log they are reading is incomplete.
/// </summary>
public sealed class LogMetricsTests
{
    [Fact]
    public async Task CountsWrittenEntries()
    {
        await using var harness = new LoggingHarness();
        using var collector = new Collector(harness.Get<LogMetrics>());

        harness.Logger().LogInformation("one");
        harness.Logger().LogInformation("two");
        await harness.RunUntilStoredAsync(2);

        collector.Collect();
        Assert.Equal(2, collector.Total("rask.logs.written"));
    }

    [Fact]
    public async Task CountsEntriesDroppedByAFullBuffer()
    {
        await using var harness = new LoggingHarness(o => o.QueueCapacity = 2);
        using var collector = new Collector(harness.Get<LogMetrics>());

        var logger = harness.Logger();
        for (var i = 0; i < 10; i++)
        {
            logger.LogInformation("entry {Index}", i);
        }

        collector.Collect();
        Assert.Equal(8, collector.Total("rask.logs.dropped"));
    }

    [Fact]
    public async Task CountsEntriesRemovedByRetention()
    {
        await using var harness = new LoggingHarness(o =>
        {
            o.Retention = TimeSpan.FromDays(1);
            o.MaxRows = 0;
            o.PurgeInterval = TimeSpan.FromMinutes(1);
        });
        using var collector = new Collector(harness.Get<LogMetrics>());

        harness.Logger().LogInformation("ancient");
        await harness.RunUntilStoredAsync(1);

        harness.Clock.Advance(TimeSpan.FromDays(2));
        await harness.RunUntilAsync(async () => await harness.Store.CountAsync() == 0);

        collector.Collect();
        Assert.Equal(1, collector.Total("rask.logs.purged"));
    }

    /// <summary>
    /// The stored-count gauge is sampled by the writer's own sweep rather than answered with a
    /// <c>COUNT(*)</c> per observation, so a collector attaching to it must not put a read on the log file
    /// on every tick. What this checks is that the sample still arrives.
    /// </summary>
    [Fact]
    public async Task SamplesTheStoredCountWhileSomethingIsListening()
    {
        await using var harness = new LoggingHarness();
        using var collector = new Collector(harness.Get<LogMetrics>());

        harness.Logger().LogInformation("one");
        await harness.RunUntilAsync(async () =>
        {
            if (await harness.Store.CountAsync() == 0)
            {
                return false;
            }

            collector.Collect();
            return collector.Last("rask.logs.stored") == 1;
        });

        Assert.Equal(1, collector.Last("rask.logs.stored"));
    }

    /// <summary>Scoped to the meter INSTANCE: xUnit runs test classes in parallel, each with an
    /// equally-named meter, so a name filter would collect other tests' measurements too.</summary>
    private sealed class Collector : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly Lock _gate = new();
        private readonly Dictionary<string, long> _totals = [];
        private readonly Dictionary<string, long> _last = [];

        public Collector(LogMetrics metrics)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument.Meter, metrics.Meter))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };

            _listener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
            {
                lock (_gate)
                {
                    _totals[instrument.Name] = _totals.GetValueOrDefault(instrument.Name) + measurement;
                    _last[instrument.Name] = measurement;
                }
            });

            _listener.SetMeasurementEventCallback<int>((instrument, measurement, _, _) =>
            {
                lock (_gate)
                {
                    _totals[instrument.Name] = _totals.GetValueOrDefault(instrument.Name) + measurement;
                    _last[instrument.Name] = measurement;
                }
            });

            _listener.Start();
        }

        public void Collect() => _listener.RecordObservableInstruments();

        public long Total(string instrument)
        {
            lock (_gate) { return _totals.GetValueOrDefault(instrument); }
        }

        public long Last(string instrument)
        {
            lock (_gate) { return _last.GetValueOrDefault(instrument); }
        }

        public void Dispose() => _listener.Dispose();
    }
}
