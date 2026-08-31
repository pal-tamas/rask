using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Rask.Outbox.Tests;

/// <summary>
/// The outbox pillar's metrics, driven through the real processor — instrumentation that isn't wired into
/// the drain would pass any test that called the meter directly.
/// </summary>
[Collection(OutboxDbCollection.Name)]
public sealed class OutboxMetricsTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-outbox-metrics-{Guid.NewGuid():N}.db");
    private readonly Recorder _recorder = new();
    private ServiceProvider _provider = null!;

    [Fact]
    public async Task Publishing_records_a_processed_count_tagged_by_event_type()
    {
        Build();
        using var collector = new Collector(_provider.GetRequiredService<OutboxMetrics>());

        await SaveAsync(Order.Place("ada"));
        await RunUntilAsync(() => collector.Sum("rask.outbox.processed") >= 1);

        Assert.Equal(1, collector.Sum("rask.outbox.processed"));
        Assert.True(collector.Count("rask.outbox.duration") >= 1);

        // Tagged by the registered event type — a closed set, so this can't explode in cardinality.
        Assert.Contains("OrderPlaced", collector.LastTag("rask.outbox.processed", "message.type") ?? "",
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unregistered_event_type_counts_failures_and_then_a_dead_letter()
    {
        // MaxAttempts 2: the drain records "no registered type" as a failed attempt each poll, so the
        // second one exhausts it. This is the path that produces a dead letter with no handler involved.
        Build(o => o.MaxAttempts = 2);
        using var collector = new Collector(_provider.GetRequiredService<OutboxMetrics>());

        await using (var db = NewContext())
        {
            db.Set<OutboxMessage>().Add(new OutboxMessage
            {
                Type = "Nothing.Registered.Here",
                Payload = "{}",
                OccurredAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await RunUntilAsync(() => collector.Sum("rask.outbox.deadlettered") >= 1);

        Assert.Equal(2, collector.Sum("rask.outbox.failed"));
        Assert.Equal(1, collector.Sum("rask.outbox.deadlettered"));
    }

    [Fact]
    public async Task The_pending_gauge_only_costs_a_query_while_someone_is_collecting()
    {
        Build();
        var metrics = _provider.GetRequiredService<OutboxMetrics>();

        Assert.False(metrics.WantsQueueDepth);

        using var collector = new Collector(_provider.GetRequiredService<OutboxMetrics>());
        Assert.True(metrics.WantsQueueDepth);

        await SaveAsync(Order.Place("grace"));
        await RunUntilAsync(() =>
        {
            collector.CollectObservable();
            return collector.LastValue("rask.outbox.pending") == 0;
        });

        // Drained, so nothing pending and nothing dead.
        collector.CollectObservable();
        Assert.Equal(0, collector.LastValue("rask.outbox.pending"));
        Assert.Equal(0, collector.LastValue("rask.outbox.deadletters"));
    }

    public void Dispose()
    {
        _provider?.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private void Build(Action<OutboxOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_recorder);
        services.AddSingleton(new @event.KeywordRecorder());
        services.AddRaskCqrs();
        services.AddRaskData();
        services.AddRaskOutbox<OutboxDbContext>(o =>
        {
            o.PollInterval = TimeSpan.FromMilliseconds(50);
            configure?.Invoke(o);
        });
        services.AddDbContextFactory<OutboxDbContext>((sp, o) => o
            .UseSqlite($"Data Source={_dbPath}")
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));

        _provider = services.BuildServiceProvider();
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    private OutboxDbContext NewContext() =>
        _provider.GetRequiredService<IDbContextFactory<OutboxDbContext>>().CreateDbContext();

    private async Task SaveAsync(Order order)
    {
        await using var db = NewContext();
        db.Orders.Add(order);
        await db.SaveChangesAsync();
    }

    private async Task RunUntilAsync(Func<bool> until)
    {
        var processor = _provider.GetServices<IHostedService>().OfType<OutboxProcessor<OutboxDbContext>>().Single();
        await processor.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (!until())
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("Metric never reached the expected value.");
                }

                await Task.Delay(25);
            }
        }
        finally
        {
            await processor.StopAsync(CancellationToken.None);
        }
    }

    private sealed class Collector : IDisposable
    {
        private readonly MeterListener _listener;
        private readonly Lock _gate = new();
        private readonly Dictionary<string, long> _sums = [];
        private readonly Dictionary<string, int> _counts = [];
        private readonly Dictionary<string, int> _lastValue = [];
        private readonly Dictionary<string, string?> _lastTag = [];

        public Collector(OutboxMetrics metrics)
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
                    Tag(instrument.Name, tags);
                }
            });
            _listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) =>
            {
                lock (_gate)
                {
                    _counts[instrument.Name] = _counts.GetValueOrDefault(instrument.Name) + 1;
                    Tag(instrument.Name, tags);
                }
            });
            _listener.SetMeasurementEventCallback<int>((instrument, value, _, _) =>
            {
                lock (_gate) { _lastValue[instrument.Name] = value; }
            });

            _listener.Start();
        }

        public long Sum(string name)
        {
            lock (_gate) { return _sums.GetValueOrDefault(name); }
        }

        public int Count(string name)
        {
            lock (_gate) { return _counts.GetValueOrDefault(name); }
        }

        public int LastValue(string name)
        {
            lock (_gate) { return _lastValue.GetValueOrDefault(name, -1); }
        }

        public string? LastTag(string name, string tag)
        {
            lock (_gate) { return _lastTag.GetValueOrDefault($"{name}/{tag}"); }
        }

        public void CollectObservable() => _listener.RecordObservableInstruments();

        public void Dispose() => _listener.Dispose();

        private void Tag(string name, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            foreach (var tag in tags)
            {
                _lastTag[$"{name}/{tag.Key}"] = tag.Value?.ToString();
            }
        }
    }
}
