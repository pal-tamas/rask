using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rask.Background.Tests;

namespace Rask.Providers.E2E.Tests;

/// <summary>The jobs table as an app's migrations left it before <c>rask db add AddJobStop</c>: no Stopped, no StartedAt.</summary>
internal static class BeforeJobStop
{
    internal static void Shape(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Job>().Ignore(nameof(Job.Stopped)).Ignore(nameof(Job.StartedAt));
}

/// <summary>
/// <c>Jobs.Stop</c>, the limits, the watch and <c>Jobs.Lanes</c> on a real server, written once and run on each provider by the
/// sealed classes below it.
/// </summary>
/// <remarks>
/// <c>Rask.Jobs.Tests</c> proves the behaviour on SQLite. What only a server can prove is the provider half: the new columns'
/// migration on a table that already holds jobs, the enum and the timestamp as each provider stores them, the conditional
/// updates (<c>Stopped IS NULL</c>) as each provider spells them, and the lane view's one query with its list of lanes. The
/// processor's clock is a fake, moved by hand; only its poll ticks are real, and short.
/// </remarks>
/// <typeparam name="TContext">The app's context, on the current model.</typeparam>
/// <typeparam name="TBefore">The same context as it was before the upgrade.</typeparam>
public abstract class JobStopProviderTests<TContext, TBefore> : IAsyncLifetime
    where TContext : DbContext
    where TBefore : DbContext
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    // Sub-microsecond on purpose: PostgreSQL keeps microseconds, SQL Server's datetime2 keeps the whole tick.
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 3, 1, 8, 30, 15, TimeSpan.Zero).AddTicks(1_234_567));
    private readonly List<ServiceProvider> _hosts = [];
    private readonly List<IHostedService> _started = [];

    private protected JobStopStage Stage { get; } = new();

    private protected JobStopReads Reads { get; } = new();

    private protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    private protected abstract bool Available { get; }

    private protected abstract string SkipReason { get; }

    /// <summary>Where the jobs table lives: the schema information_schema names it under.</summary>
    private protected abstract string TableSchema { get; }

    /// <summary>How the provider stores <see cref="Job.Stopped" /> and <see cref="Job.StartedAt" />, as information_schema says it.</summary>
    private protected abstract (string Stopped, string StartedAt) ColumnTypes { get; }

    /// <summary>The finest time the provider keeps: what a timestamp written and read back may lose.</summary>
    private protected abstract TimeSpan Precision { get; }

    private protected abstract void Use(DbContextOptionsBuilder builder);

    private protected abstract TContext NewContext();

    private protected abstract TBefore NewContextBeforeUpgrade();

    /// <summary>Drops whatever <paramref name="db" />'s tables live in, and creates them again from its model.</summary>
    private protected abstract Task Recreate(DbContext db);

    private protected abstract Task Drop(DbContext db);

    public async ValueTask InitializeAsync()
    {
        if (Available)
        {
            await using var db = NewContext();
            await Recreate(db);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Stage.ReleaseAll();
        foreach (var processor in _started)
        {
            await processor.StopAsync(CancellationToken.None);
        }

        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
        }

        if (Available)
        {
            await using var db = NewContext();
            await Drop(db);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Upgrading_a_table_that_holds_jobs_says_which_migration_is_missing_then_keeps_every_row_and_runs_the_due_ones()
    {
        Assert.SkipUnless(Available, SkipReason);
        await using (var before = NewContextBeforeUpgrade())
        {
            await Recreate(before);
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        var pending = await InsertBeforeUpgrade(new NoteJob("pending"), "pump-1");
        var running = await InsertBeforeUpgrade(new NoteJob("running"), "pump-2", s => s
            .SetProperty(j => j.ClaimToken, Guid.NewGuid())
            .SetProperty(j => j.ClaimedUntil, now - Minute) // its host went down with the old version
            .SetProperty(j => j.Attempts, 1));
        var processed = await InsertBeforeUpgrade(new NoteJob("processed"), null, s => s
            .SetProperty(j => j.ProcessedAt, now - Minute)
            .SetProperty(j => j.Attempts, 1));
        var dead = await InsertBeforeUpgrade(new NoteJob("dead"), "pump-3", s => s
            .SetProperty(j => j.Attempts, 25)
            .SetProperty(j => j.Error, "boom"));
        var stale = NewHost();
        await Start(stale);
        await JobStopWait.Until(() => Task.FromResult(Stage.Logs.Any(l => l.Contains("rask db add AddJobStop", StringComparison.Ordinal))));
        await Stop(stale);

        await using (var from = NewContextBeforeUpgrade())
        await using (var to = NewContext())
        {
            await Migrate(from, to);
        }

        var stoppedType = await ColumnType(nameof(Job.Stopped));
        var startedAtType = await ColumnType(nameof(Job.StartedAt));
        var upgraded = await Rows(pending, running, processed, dead);
        var host = NewHost();
        await Start(host);
        await JobStopWait.Until(async () => (await Row(pending))?.ProcessedAt is not null && (await Row(running))?.ProcessedAt is not null);
        var after = await Rows(pending, running, processed, dead);

        var hint = Stage.Logs.First(l => l.Contains("rask db add AddJobStop", StringComparison.Ordinal));
        Assert.Contains("Stopped and StartedAt columns", hint, StringComparison.Ordinal);
        Assert.Contains("rask db update", hint, StringComparison.Ordinal);
        Assert.Equal($"{ColumnTypes.Stopped}:YES", stoppedType);
        Assert.Equal($"{ColumnTypes.StartedAt}:YES", startedAtType);
        Assert.All(upgraded, row => Assert.Null(row.Stopped));
        Assert.All(upgraded, row => Assert.Null(row.StartedAt));
        Assert.Equal(["pending:done", "running:done"], Stage.Notes.Order(StringComparer.Ordinal));
        Assert.All(after.Take(2), row =>
        {
            Assert.Null(row.Stopped);
            Assert.Null(row.ClaimToken);
            Assert.Null(row.Error);
            AssertSameMoment(_clock.GetUtcNow().UtcDateTime, row.StartedAt);
        });
        Assert.Equal(2, after[1].Attempts);
        Assert.Equal(upgraded[2].ProcessedAt, after[2].ProcessedAt);
        Assert.Null(after[2].StartedAt);
        Assert.Null(after[3].ProcessedAt);
        Assert.Equal((25, "boom"), (after[3].Attempts, after[3].Error));
        Assert.Null(after[3].Stopped);
    }

    [Fact]
    public async Task Many_lanes_are_read_in_one_query_with_what_runs_and_what_waits_in_the_order_each_lane_takes_it()
    {
        Assert.SkipUnless(Available, SkipReason);
        var host = NewHost();
        var jobs = host.GetRequiredService<IJobs>();
        var now = _clock.GetUtcNow().UtcDateTime;
        var later = await Enqueue(jobs, new HoldJob("a-later"), "pump-a", TimeSpan.FromMinutes(2));
        var running = await Enqueue(jobs, new HoldJob("a-running"), "pump-a");
        var second = await Enqueue(jobs, new HoldJob("a-second"), "pump-a");
        var third = await Enqueue(jobs, new HoldJob("a-third"), "pump-a");
        var first = await Enqueue(jobs, new HoldJob("a-first"), "pump-a");
        var dead = await Enqueue(jobs, new HoldJob("a-dead"), "pump-a");
        var done = await Enqueue(jobs, new HoldJob("a-done"), "pump-a");
        var expired = await Enqueue(jobs, new HoldJob("b-expired"), "pump-b");
        var next = await Enqueue(jobs, new HoldJob("b-next"), "pump-b");
        var lastTry = await Enqueue(jobs, new HoldJob("c-last-try"), "pump-c");
        await Enqueue(jobs, new HoldJob("z"), "pump-z");
        await Update(running, s => s
            .SetProperty(j => j.ClaimToken, Guid.NewGuid())
            .SetProperty(j => j.ClaimedUntil, now + TimeSpan.FromMinutes(5))
            .SetProperty(j => j.StartedAt, now)
            .SetProperty(j => j.Attempts, 1));
        await Update(first, s => s.SetProperty(j => j.RunAt, now - Minute));
        await Update(dead, s => s.SetProperty(j => j.Attempts, 25).SetProperty(j => j.Error, "boom"));
        await Update(done, s => s.SetProperty(j => j.ProcessedAt, now));
        await Update(expired, s => s
            .SetProperty(j => j.ClaimToken, Guid.NewGuid())
            .SetProperty(j => j.ClaimedUntil, now - Minute)
            .SetProperty(j => j.StartedAt, now - TimeSpan.FromMinutes(10))
            .SetProperty(j => j.Attempts, 1));
        await Update(lastTry, s => s
            .SetProperty(j => j.ClaimToken, Guid.NewGuid())
            .SetProperty(j => j.ClaimedUntil, now + TimeSpan.FromMinutes(5))
            .SetProperty(j => j.StartedAt, now)
            .SetProperty(j => j.Attempts, 25));
        var reads = Reads.Count;

        var lanes = await jobs.Lanes(["pump-a", "pump-b", "pump-c", "pump-idle", "pump-a"], Ct);

        Assert.Equal(reads + 1, Reads.Count);
        Assert.Equal(["pump-a", "pump-b", "pump-c", "pump-idle"], lanes.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(running, lanes["pump-a"].Running?.Id);
        Assert.Equal(new HoldJob("a-running"), lanes["pump-a"].Running?.Job);
        AssertSameMoment(now, lanes["pump-a"].Running?.Started);
        Assert.Equal([first, second, third, later], lanes["pump-a"].Waiting.Select(j => j.Id));
        Assert.Null(lanes["pump-b"].Running);
        Assert.Equal([expired, next], lanes["pump-b"].Waiting.Select(j => j.Id));
        Assert.Equal(lastTry, lanes["pump-c"].Running?.Id);
        Assert.Empty(lanes["pump-c"].Waiting);
        Assert.True(lanes["pump-idle"].IsIdle);
    }

    [Fact]
    public async Task Stopping_a_running_lane_job_ends_it_asked_gives_its_claim_back_and_the_lane_runs_its_next_job()
    {
        Assert.SkipUnless(Available, SkipReason);
        var host = NewHost();
        var jobs = host.GetRequiredService<IJobs>();
        var stopped = await Enqueue(jobs, new HoldJob("first"), "pump-1");
        var next = await Enqueue(jobs, new HoldJob("next"), "pump-1");
        await Start(host);
        await Stage.Entered("first");
        var lane = (await jobs.Lanes(["pump-1"], Ct))["pump-1"];

        var asked = await jobs.Stop(stopped, Ct);
        await Stage.Entered("next");
        var row = await Row(stopped);
        var again = await jobs.Stop(stopped, Ct);
        Stage.Release("next");
        await JobStopWait.Until(async () => (await Row(next))?.ProcessedAt is not null);

        Assert.Equal(stopped, lane.Running?.Id);
        Assert.Equal([next], lane.Waiting.Select(j => j.Id));
        Assert.True(asked);
        Assert.False(again);
        Assert.Equal(JobStop.Asked, row!.Stopped);
        Assert.NotNull(row.ProcessedAt);
        Assert.Null(row.ClaimToken);
        Assert.Null(row.ClaimedUntil);
        Assert.Null(row.Error);
        Assert.Equal(1, row.Attempts);
        Assert.Equal(["first:stopped:Asked", "next:done"], Stage.Notes);
        Assert.Null((await Row(next))!.Stopped);
        Assert.True((await jobs.Lanes(["pump-1"], Ct))["pump-1"].IsIdle);
    }

    [Fact]
    public async Task Stopping_a_waiting_job_ends_it_at_once_and_it_never_runs()
    {
        Assert.SkipUnless(Available, SkipReason);
        var host = NewHost();
        var jobs = host.GetRequiredService<IJobs>();
        await Enqueue(jobs, new HoldJob("first"), "pump-1");
        var waiting = await Enqueue(jobs, new HoldJob("waiting"), "pump-1");
        var after = await Enqueue(jobs, new HoldJob("after"), "pump-1");
        await Start(host);
        await Stage.Entered("first");

        var asked = await jobs.Stop(waiting, Ct);
        var row = await Row(waiting);
        Stage.Release("first");
        await Stage.Entered("after");
        Stage.Release("after");
        await JobStopWait.Until(async () => (await Row(after))?.ProcessedAt is not null);

        Assert.True(asked);
        Assert.Equal(JobStop.Asked, row!.Stopped);
        Assert.NotNull(row.ProcessedAt);
        Assert.Equal(0, row.Attempts);
        Assert.Null(row.StartedAt);
        Assert.Null(row.ClaimToken);
        Assert.Equal(["first:done", "after:done"], Stage.Notes);
        Assert.False(Stage.HasEntered("waiting"));
    }

    [Fact]
    public async Task A_job_past_its_time_limit_is_cut_at_the_next_look_and_its_row_says_why()
    {
        Assert.SkipUnless(Available, SkipReason);
        var host = NewHost(o => o.Limit<HoldJob>().After(TimeSpan.FromMinutes(10)));
        var jobs = host.GetRequiredService<IJobs>();
        var id = await Enqueue(jobs, new HoldJob("a"), "pump-1");
        await Start(host);
        await Stage.Entered("a");

        _clock.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(1));
        await Watched(host);
        var oneSecondShort = await Row(id);
        _clock.Advance(TimeSpan.FromSeconds(1));
        var row = await Processed(id);

        Assert.Null(oneSecondShort!.Stopped);
        Assert.Null(oneSecondShort.ProcessedAt);
        Assert.Equal(JobStop.TimeLimit, row.Stopped);
        Assert.Null(row.ClaimToken);
        Assert.Null(row.Error);
        Assert.Equal(["a:stopped:TimeLimit"], Stage.Notes);
    }

    [Fact]
    public async Task A_job_that_reports_no_progress_for_its_idle_limit_is_cut_and_its_row_says_why()
    {
        Assert.SkipUnless(Available, SkipReason);
        var host = NewHost(o => o.Limit<PulseJob>().IdleFor(TimeSpan.FromMinutes(5)));
        var jobs = host.GetRequiredService<IJobs>();
        var id = await Enqueue(jobs, new PulseJob("a"), "pump-1");
        await Start(host);
        await Stage.Entered("a");

        _clock.Advance(TimeSpan.FromMinutes(4));
        await Stage.Pulse("a");
        _clock.Advance(TimeSpan.FromMinutes(4));
        await Watched(host);
        var stillReporting = await Row(id);
        _clock.Advance(Minute);
        var row = await Processed(id);

        Assert.Null(stillReporting!.ProcessedAt);
        Assert.Null(stillReporting.Stopped);
        Assert.Equal(JobStop.Idle, row.Stopped);
        Assert.Null(row.ClaimToken);
        Assert.Equal(["a:stopped:Idle"], Stage.Notes);
    }

    [Fact]
    public async Task A_stop_asked_after_a_limit_was_written_is_refused_and_the_limit_stands()
    {
        Assert.SkipUnless(Available, SkipReason);
        var host = NewHost(o => o.Limit<DeafJob>().After(TimeSpan.FromMinutes(10)));
        var jobs = host.GetRequiredService<IJobs>();
        var id = await Enqueue(jobs, new DeafJob("a"), "pump-1");
        await Start(host);
        await Stage.Entered("a");
        _clock.Advance(TimeSpan.FromMinutes(10));
        await JobStopWait.Until(async () => (await Row(id))?.Stopped is not null);

        var asked = await jobs.Stop(id, Ct);
        var marked = await Row(id);
        Stage.Release("a");
        var row = await Processed(id);

        Assert.False(asked);
        Assert.Equal(JobStop.TimeLimit, marked!.Stopped);
        Assert.Null(marked.ProcessedAt);
        Assert.NotNull(marked.ClaimToken);
        Assert.Equal(JobStop.TimeLimit, row.Stopped);
        Assert.Equal(["a:returned:TimeLimit"], Stage.Notes);
    }

    [Fact]
    public async Task A_stop_asked_from_another_host_reaches_the_job_at_the_next_look_and_stands_over_a_limit_passed_after_it()
    {
        Assert.SkipUnless(Available, SkipReason);
        var host = NewHost(o => o.Limit<DeafJob>().After(TimeSpan.FromMinutes(10)));
        var other = NewHost().GetRequiredService<IJobs>();
        var id = await Enqueue(host.GetRequiredService<IJobs>(), new DeafJob("a"), "pump-1");
        await Start(host);
        await Stage.Entered("a");

        var asked = await other.Stop(id, Ct);
        _clock.Advance(TimeSpan.FromMinutes(10));
        await Watched(host);
        var again = await other.Stop(id, Ct);
        var marked = await Row(id);
        Stage.Release("a");
        var row = await Processed(id);

        Assert.True(asked);
        Assert.False(again);
        Assert.Equal(JobStop.Asked, marked!.Stopped);
        Assert.Null(marked.ProcessedAt);
        Assert.Equal(JobStop.Asked, row.Stopped);
        Assert.Null(row.ClaimToken);
        Assert.Equal(["a:returned:Asked"], Stage.Notes);
    }

    [Fact]
    public async Task A_job_whose_row_is_deleted_from_outside_stops_within_one_look_and_its_lane_goes_on()
    {
        Assert.SkipUnless(Available, SkipReason);
        var host = NewHost();
        var jobs = host.GetRequiredService<IJobs>();
        var id = await Enqueue(jobs, new HoldJob("a"), "pump-1");
        var next = await Enqueue(jobs, new NoteJob("next"), "pump-1");
        await Start(host);
        await Stage.Entered("a");

        await using (var db = NewContext())
        {
            await db.Set<Job>().Where(j => j.Id == id).ExecuteDeleteAsync(Ct);
        }

        await JobStopWait.Until(async () => (await Row(next))?.ProcessedAt is not null);

        Assert.Null(await Row(id));
        Assert.Equal(["a:stopped:DoneElsewhere", "next:done"], Stage.Notes);
    }

    [Fact]
    public async Task A_job_whose_row_is_marked_done_from_outside_stops_within_one_look_and_the_row_is_left_as_they_wrote_it()
    {
        Assert.SkipUnless(Available, SkipReason);
        var host = NewHost();
        var jobs = host.GetRequiredService<IJobs>();
        var id = await Enqueue(jobs, new HoldJob("a"), "pump-1");
        var next = await Enqueue(jobs, new NoteJob("next"), "pump-1");
        await Start(host);
        await Stage.Entered("a");
        var doneAt = _clock.GetUtcNow().UtcDateTime - TimeSpan.FromHours(1);

        await Update(id, s => s.SetProperty(j => j.ProcessedAt, doneAt).SetProperty(j => j.Error, "fixed by hand"));
        await JobStopWait.Until(async () => (await Row(next))?.ProcessedAt is not null);
        var row = await Row(id);

        Assert.Equal(["a:stopped:DoneElsewhere", "next:done"], Stage.Notes);
        AssertSameMoment(doneAt, row!.ProcessedAt);
        Assert.Equal("fixed by hand", row.Error);
        Assert.Null(row.Stopped);
        Assert.NotNull(row.ClaimToken);
    }

    private ServiceProvider NewHost(Action<JobsOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(new JobStopLogs(Stage.Logs)));
        services.AddSingleton(Stage);
        services.AddSingleton<TimeProvider>(_clock); // first, so AddRaskJobs keeps it
        services.AddRaskCqrs();
        services.AddRaskJobs<TContext>(o =>
        {
            o.PollInterval = TimeSpan.FromMilliseconds(50);
            // Longer than any test moves the clock, so a claim never lapses under a job still running.
            o.LeaseDuration = TimeSpan.FromHours(2);
            configure?.Invoke(o);
        });
        services.AddDbContextFactory<TContext>(o =>
        {
            Use(o);
            o.AddInterceptors(Reads);
        });

        var provider = services.BuildServiceProvider();
        _hosts.Add(provider);
        return provider;
    }

    private static JobProcessor<TContext> ProcessorOf(ServiceProvider host) =>
        host.GetServices<IHostedService>().OfType<JobProcessor<TContext>>().Single();

    private async Task Start(ServiceProvider host)
    {
        var processor = ProcessorOf(host);
        _started.Add(processor);
        await processor.StartAsync(CancellationToken.None);
    }

    private async Task Stop(ServiceProvider host)
    {
        var processor = ProcessorOf(host);
        _started.Remove(processor);
        await processor.StopAsync(CancellationToken.None);
    }

    /// <summary>Waits until the watch has looked twice more, so a whole look started after the test moved the clock.</summary>
    private static Task Watched(ServiceProvider host)
    {
        var processor = ProcessorOf(host);
        var seen = processor.Looks;
        return JobStopWait.Until(() => Task.FromResult(processor.Looks >= seen + 2));
    }

    private async Task<long> Enqueue(IJobs jobs, IJob job, string lane, TimeSpan? delay = null)
    {
        if (delay is { } wait)
        {
            await jobs.Enqueue(job, Ct).InLane(lane).In(wait);
        }
        else
        {
            await jobs.Enqueue(job, Ct).InLane(lane);
        }

        // Each test runs alone on tables of its own, so the newest row is the one just written.
        await using var db = NewContext();
        return await db.Set<Job>().MaxAsync(j => j.Id, Ct);
    }

    private async Task<long> InsertBeforeUpgrade(IJob job, string? lane, Action<UpdateSettersBuilder<Job>>? state = null)
    {
        var (type, payload) = JobSerializerRegistry.Serialize(job);
        var row = Job.For(type, payload, _clock.GetUtcNow().UtcDateTime - Minute, lane);
        await using var db = NewContextBeforeUpgrade();
        db.Set<Job>().Add(row);
        await db.SaveChangesAsync(Ct);
        if (state is not null)
        {
            await db.Set<Job>().Where(j => j.Id == row.Id).ExecuteUpdateAsync(state, Ct);
        }

        return row.Id;
    }

    private async Task Update(long id, Action<UpdateSettersBuilder<Job>> set)
    {
        await using var db = NewContext();
        await db.Set<Job>().Where(j => j.Id == id).ExecuteUpdateAsync(set, Ct);
    }

    private async Task<Job?> Row(long id)
    {
        await using var db = NewContext();
        return await db.Set<Job>().AsNoTracking().SingleOrDefaultAsync(j => j.Id == id, Ct);
    }

    private async Task<List<Job>> Rows(params long[] ids)
    {
        var rows = new List<Job>();
        foreach (var id in ids)
        {
            rows.Add((await Row(id))!);
        }

        return rows;
    }

    private async Task<Job> Processed(long id)
    {
        await JobStopWait.Until(async () => (await Row(id))?.ProcessedAt is not null);
        return (await Row(id))!;
    }

    private async Task<string> ColumnType(string column)
    {
        await using var db = NewContext();
        return await db.Database
            .SqlQuery<string>($"""
                SELECT CONCAT(data_type, ':', is_nullable) AS "Value" FROM information_schema.columns
                WHERE table_schema = {TableSchema} AND table_name = 'Job' AND column_name = {column}
                """)
            .SingleAsync(Ct);
    }

    // What `rask db add AddJobStop` writes and `rask db update` runs: the differ between the two models, then the provider's SQL.
    private static async Task Migrate(DbContext from, DbContext to)
    {
        var model = to.GetService<IDesignTimeModel>().Model;
        var operations = to.GetService<IMigrationsModelDiffer>().GetDifferences(
            from.GetService<IDesignTimeModel>().Model.GetRelationalModel(), model.GetRelationalModel());
        var commands = to.GetService<IMigrationsSqlGenerator>().Generate(operations, model);
        await to.GetService<IMigrationCommandExecutor>().ExecuteNonQueryAsync(commands, to.GetService<IRelationalConnection>(), Ct);
    }

    private void AssertSameMoment(DateTime expected, DateTime? actual)
    {
        Assert.NotNull(actual);
        var lost = (expected - actual.Value).Duration();
        Assert.True(lost < Precision, $"{actual.Value:O} is {lost} away from {expected:O}; the provider keeps {Precision}.");
    }
}
