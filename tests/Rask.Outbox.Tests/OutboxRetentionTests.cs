using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rask.Tests.Shared;

namespace Rask.Outbox.Tests;

/// <summary>
/// Retention. The outbox was the only DB-backed pillar with none, so its table grew for the life of the
/// application — every domain event ever raised, payload included, on the same SQLite file the app serves
/// from (and that Litestream replicates and snapshots copy).
/// </summary>
public sealed class OutboxRetentionTests : IDisposable
{
    private readonly InMemorySqlite _db = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
    private ServiceProvider _provider = null!;

    [Fact]
    public async Task Published_messages_older_than_the_retention_period_are_purged()
    {
        Build(o => o.Retention = TimeSpan.FromDays(7));
        var now = _clock.GetUtcNow().UtcDateTime;

        await SeedAsync(
            Message(processedAt: now.AddDays(-10)),   // published, stale  -> goes
            Message(processedAt: now.AddDays(-1)));   // published, recent -> stays

        await RunCycleAsync();

        var survivor = Assert.Single(await AllAsync());
        Assert.Equal(Timestamp.Create(now.AddDays(-1)), survivor.ProcessedAt);
    }

    [Fact]
    public async Task A_dead_letter_is_never_purged_however_old_it_is()
    {
        // The row an operator still needs. It has no ProcessedAt, so the retention predicate can't see it —
        // and that has to hold for a message far older than any cutoff.
        Build(o => o.Retention = TimeSpan.FromDays(7));
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
        Build(o => o.Retention = TimeSpan.Zero);
        var now = _clock.GetUtcNow().UtcDateTime;

        await SeedAsync(Message(processedAt: now.AddYears(-5)));

        await RunCycleAsync();

        Assert.Single(await AllAsync());
    }

    [Fact]
    public async Task Times_either_side_of_the_autumn_hour_are_stored_and_purged_as_the_utc_instants_they_are()
    {
        // In Budapest both rows were published at 02:30 on the wall: only their instants tell them apart, and the
        // cutoff (01:00Z) falls between them.
        using var zone = AmbientClock.UseZone(TimeZoneInfo.FindSystemTimeZoneById("Europe/Budapest"));
        _clock.Advance(new DateTimeOffset(2026, 11, 1, 1, 0, 0, TimeSpan.Zero) - _clock.GetUtcNow());
        Build(o => o.Retention = TimeSpan.FromDays(7));
        var first = new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc);
        var second = new DateTime(2026, 10, 25, 1, 30, 0, DateTimeKind.Utc);
        await SeedAsync(Message(occurredAt: first, processedAt: first), Message(occurredAt: second, processedAt: second));

        await RunCycleAsync();

        var survivor = Assert.Single(await AllAsync());
        Assert.Equal(new DateTimeOffset(second), survivor.OccurredAt.Instant);
        Assert.Equal(new DateTimeOffset(second), survivor.ProcessedAt!.Instant);
        Assert.Equal(new DateTime(2026, 10, 25, 2, 30, 0), survivor.ProcessedAt.Value);
        Assert.Equal(["2026-10-25 01:30:00"], await StoredProcessedAtAsync());
    }

    [Fact]
    public async Task Retention_defaults_to_seven_days_matching_jobs_and_mail()
    {
        Assert.Equal(TimeSpan.FromDays(7), new OutboxOptions().Retention);
    }

    [Fact]
    public async Task A_backlog_larger_than_one_page_is_fully_drained_in_a_single_sweep()
    {
        // The first sweep on an app that has been running without retention has to catch up rather than
        // remove one page an hour — otherwise a large table never shrinks.
        Build(o => o.Retention = TimeSpan.FromDays(7));
        var now = _clock.GetUtcNow().UtcDateTime;

        await SeedAsync([.. Enumerable.Range(0, 2500).Select(_ => Message(processedAt: now.AddDays(-30)))]);

        await RunCycleAsync();

        Assert.Empty(await AllAsync());
    }

    [Fact]
    public async Task The_sweep_runs_at_most_once_an_hour()
    {
        Build(o => o.Retention = TimeSpan.FromDays(7));
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
        _db.Dispose();
    }

    private void Build(Action<OutboxOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddRaskCqrs();
        services.AddRaskData();
        services.AddRaskOutbox<OutboxDbContext>(o =>
        {
            o.PollInterval = TimeSpan.FromMilliseconds(50);
            configure(o);
        });
        services.AddDbContextFactory<OutboxDbContext>((sp, o) => o
            .UseSqlite(_db.ConnectionString)
            .AddInterceptors(sp.GetServices<ISaveChangesInterceptor>()));

        _provider = services.BuildServiceProvider();
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    // One poll cycle, deterministically: start the processor, wait for the sweep, stop. Driving the real
    // hosted service (rather than calling a private method) is what proves the purge is actually wired
    // into the cycle — a PurgeAsync nobody calls would pass every other assertion here.
    // Drives exactly one poll, rather than starting the hosted service and sleeping for a fixed 500 ms.
    // That sleep was a race against the machine: under a full-suite load the first cycle had not finished
    // when the processor was stopped, so the retention sweep never ran and the assertions below read as
    // "the sweep is broken" instead of "the sweep never happened". Retention is throttled on the injected
    // clock, so one explicit cycle per step is exactly what these tests mean.
    private Task RunCycleAsync()
    {
        var processor = _provider.GetServices<IHostedService>().OfType<OutboxProcessor<OutboxDbContext>>().Single();
        return processor.RunCycleAsync(CancellationToken.None);
    }

    private OutboxDbContext NewContext() =>
        _provider.GetRequiredService<IDbContextFactory<OutboxDbContext>>().CreateDbContext();

    private async Task<List<OutboxMessage>> AllAsync()
    {
        await using var db = NewContext();
        return await db.Set<OutboxMessage>().OrderBy(m => m.Id).ToListAsync();
    }

    // The processed column as the database holds it, read past every converter.
    private async Task<List<string>> StoredProcessedAtAsync()
    {
        await using var db = NewContext();
        return await db.Database.SqlQueryRaw<string>("SELECT \"ProcessedAt\" AS \"Value\" FROM \"OutboxMessage\"").ToListAsync();
    }

    private async Task SeedAsync(params OutboxMessage[] messages)
    {
        await using var db = NewContext();
        db.Set<OutboxMessage>().AddRange(messages);
        await db.SaveChangesAsync();
    }

    // A row in a state the production API cannot reach on its own: retention is about rows that have
    // already been published, or already failed their way to a dead letter. Written through the entity's own
    // private setters with [UnsafeAccessor] — the same mechanism Rask's generated writes use — rather than
    // widening OutboxMessage so a test can reach them.
    private OutboxMessage Message(
        DateTime? occurredAt = null, DateTime? processedAt = null, int attempts = 0, string? error = null)
    {
        var message = OutboxMessage.For("Some.Event", "{}", "Some.Handler:Some.Event", Timestamp.Create(occurredAt ?? _clock.GetUtcNow().UtcDateTime));

        if (processedAt is { } published)
        {
            message.Published(Timestamp.Create(published));
        }

        if (error is not null)
        {
            message.Failed(error);
        }

        SetAttempts(message) = attempts;
        return message;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "<Attempts>k__BackingField")]
    private static extern ref int SetAttempts(OutboxMessage message);
}
