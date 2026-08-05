using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Rask.Outbox.Tests;

/// <summary>
/// Retention. The outbox was the only DB-backed pillar with none, so its table grew for the life of the
/// application — every domain event ever raised, payload included, on the same SQLite file the app serves
/// from (and that Litestream replicates and snapshots copy).
/// </summary>
public sealed class OutboxRetentionTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-outbox-ret-{Guid.NewGuid():N}.db");
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
    private ServiceProvider _provider = null!;

    [Fact]
    public async Task Published_messages_older_than_the_retention_period_are_purged()
    {
        Build(o => o.RetentionPeriod = TimeSpan.FromDays(7));
        var now = _clock.GetUtcNow().UtcDateTime;

        await SeedAsync(
            Message(processedAt: now.AddDays(-10)),   // published, stale  -> goes
            Message(processedAt: now.AddDays(-1)));   // published, recent -> stays

        await RunCycleAsync();

        var survivor = Assert.Single(await AllAsync());
        Assert.Equal(now.AddDays(-1), survivor.ProcessedAt);
    }

    [Fact]
    public async Task A_dead_letter_is_never_purged_however_old_it_is()
    {
        // The row an operator still needs. It has no ProcessedAt, so the retention predicate can't see it —
        // and that has to hold for a message far older than any cutoff.
        Build(o => o.RetentionPeriod = TimeSpan.FromDays(7));
        var now = _clock.GetUtcNow().UtcDateTime;

        await SeedAsync(
            Message(occurredAt: now.AddYears(-1), attempts: 10, error: "gave up"),   // dead letter
            Message(occurredAt: now.AddYears(-1)),                                   // still pending
            Message(processedAt: now.AddDays(-30)));                                 // published -> goes

        await RunCycleAsync();

        var rows = await AllAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, m => Assert.Null(m.ProcessedAt));
    }

    [Fact]
    public async Task A_non_positive_retention_period_keeps_everything_forever()
    {
        Build(o => o.RetentionPeriod = TimeSpan.Zero);
        var now = _clock.GetUtcNow().UtcDateTime;

        await SeedAsync(Message(processedAt: now.AddYears(-5)));

        await RunCycleAsync();

        Assert.Single(await AllAsync());
    }

    [Fact]
    public async Task Retention_defaults_to_seven_days_matching_jobs_and_mail()
    {
        Assert.Equal(TimeSpan.FromDays(7), new OutboxOptions().RetentionPeriod);
    }

    [Fact]
    public async Task A_backlog_larger_than_one_page_is_fully_drained_in_a_single_sweep()
    {
        // The first sweep on an app that has been running without retention has to catch up rather than
        // remove one page an hour — otherwise a large table never shrinks.
        Build(o => o.RetentionPeriod = TimeSpan.FromDays(7));
        var now = _clock.GetUtcNow().UtcDateTime;

        await SeedAsync([.. Enumerable.Range(0, 2500).Select(_ => Message(processedAt: now.AddDays(-30)))]);

        await RunCycleAsync();

        Assert.Empty(await AllAsync());
    }

    [Fact]
    public async Task The_sweep_runs_at_most_once_an_hour()
    {
        Build(o => o.RetentionPeriod = TimeSpan.FromDays(7));
        var now = _clock.GetUtcNow().UtcDateTime;

        await SeedAsync(Message(processedAt: now.AddDays(-30)));
        await RunCycleAsync();
        Assert.Empty(await AllAsync());

        // A second stale row moments later must survive: the sweep is throttled, so it isn't a per-poll
        // table scan on a 5-second timer.
        await SeedAsync(Message(processedAt: now.AddDays(-30)));
        await RunCycleAsync();
        Assert.Single(await AllAsync());

        // …and is collected once the interval has elapsed.
        _clock.Advance(TimeSpan.FromHours(2));
        await RunCycleAsync();
        Assert.Empty(await AllAsync());
    }

    public void Dispose()
    {
        _provider?.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private void Build(Action<OutboxOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddRaskCqrs();
        services.AddRaskData(o => o.DispatchDomainEventsInProcess = false);
        services.AddRaskOutbox<OutboxDbContext>(o =>
        {
            o.PollInterval = TimeSpan.FromMilliseconds(50);
            configure(o);
        });
        services.AddDbContextFactory<OutboxDbContext>((sp, o) => o
            .UseSqlite($"Data Source={_dbPath}")
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));

        _provider = services.BuildServiceProvider();
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    // One poll cycle, deterministically: start the processor, wait for the sweep, stop. Driving the real
    // hosted service (rather than calling a private method) is what proves the purge is actually wired
    // into the cycle — a PurgeAsync nobody calls would pass every other assertion here.
    private async Task RunCycleAsync()
    {
        var processor = _provider.GetServices<IHostedService>().OfType<OutboxProcessor<OutboxDbContext>>().Single();
        await processor.StartAsync(CancellationToken.None);
        await Task.Delay(500);
        await processor.StopAsync(CancellationToken.None);
    }

    private OutboxDbContext NewContext() =>
        _provider.GetRequiredService<IDbContextFactory<OutboxDbContext>>().CreateDbContext();

    private async Task<List<OutboxMessage>> AllAsync()
    {
        await using var db = NewContext();
        return await db.Set<OutboxMessage>().OrderBy(m => m.Id).ToListAsync();
    }

    private async Task SeedAsync(params OutboxMessage[] messages)
    {
        await using var db = NewContext();
        db.Set<OutboxMessage>().AddRange(messages);
        await db.SaveChangesAsync();
    }

    private OutboxMessage Message(
        DateTime? occurredAt = null, DateTime? processedAt = null, int attempts = 0, string? error = null) =>
        new()
        {
            Type = "Some.Event",
            Payload = "{}",
            OccurredAt = occurredAt ?? _clock.GetUtcNow().UtcDateTime,
            ProcessedAt = processedAt,
            Attempts = attempts,
            Error = error,
        };

    private sealed class FakeClock(DateTimeOffset start) : TimeProvider
    {
        private long _ticks = start.UtcTicks;

        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

        public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
    }
}
