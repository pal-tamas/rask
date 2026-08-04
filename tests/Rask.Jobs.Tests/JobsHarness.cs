using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Rask.Jobs.Tests;

// ── Test jobs + handlers. ────────────────────────────────────────────────────────────────────────────────
// These are top-level purely for readability; nesting is fine now that the generator keys registrations on
// the runtime metadata name rather than a C# display string. See KeywordNamespaceJob.cs for the shape that
// used to dead-letter, and Outer.NestedJob below for the '+'-vs-'.' case.

public sealed record RecordJob(string Value) : IJob;

public sealed record FailingJob : IJob;

public sealed record TickJob : IJob;

/// <summary>Thread-safe sink the job handlers write to (they run on the processor's background thread).</summary>
public sealed class Recorder
{
    private readonly List<string> _values = [];
    private int _ticks;

    public IReadOnlyList<string> Values
    {
        get { lock (_values) { return _values.ToArray(); } }
    }

    public int Ticks => Volatile.Read(ref _ticks);

    public void Add(string value)
    {
        lock (_values) { _values.Add(value); }
    }

    public void Tick() => Interlocked.Increment(ref _ticks);
}

public sealed class RecordJobHandler(Recorder recorder) : ICommandHandler<RecordJob>
{
    public Task HandleAsync(RecordJob command, CancellationToken cancellationToken)
    {
        recorder.Add(command.Value);
        return Task.CompletedTask;
    }
}

public sealed class FailingJobHandler : ICommandHandler<FailingJob>
{
    public Task HandleAsync(FailingJob command, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("boom");
}

public sealed class TickJobHandler(Recorder recorder) : ICommandHandler<TickJob>
{
    public Task HandleAsync(TickJob command, CancellationToken cancellationToken)
    {
        recorder.Tick();
        return Task.CompletedTask;
    }
}

public sealed class JobsDbContext(DbContextOptions<JobsDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddRaskJobs();
}

/// <summary>A hand-rolled fake clock (no external package): the processor's due/backoff checks read it, so
/// tests drive time deterministically while the poll loop ticks on the real (short) interval.</summary>
public sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    private long _ticks = start.UtcTicks;

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}

/// <summary>Builds a real-SQLite service provider wired for jobs, with a controllable clock.</summary>
public sealed class JobsHarness : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly bool _ownsDb;

    public JobsHarness(Action<JobOptions>? configure = null, DateTimeOffset? start = null, string? dbPath = null)
    {
        _ownsDb = dbPath is null;
        DbPath = dbPath ?? Path.Combine(Path.GetTempPath(), $"rask-jobs-test-{Guid.NewGuid():N}.db");
        Clock = new FakeTimeProvider(start ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Recorder);
        services.AddSingleton<TimeProvider>(Clock); // registered first so AddRaskJobs' TryAddSingleton keeps it
        services.AddRaskCqrs();
        services.AddRaskJobs<JobsDbContext>(o =>
        {
            o.PollInterval = TimeSpan.FromMilliseconds(20);
            configure?.Invoke(o);
        });
        services.AddDbContextFactory<JobsDbContext>(o => o.UseSqlite($"Data Source={DbPath}"));

        _provider = services.BuildServiceProvider();
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    public string DbPath { get; }

    public FakeTimeProvider Clock { get; }

    public Recorder Recorder { get; } = new();

    public IJobQueue Queue => _provider.GetRequiredService<IJobQueue>();

    public IHostedService Processor =>
        _provider.GetServices<IHostedService>().OfType<JobProcessor<JobsDbContext>>().Single();

    public JobsDbContext NewContext() =>
        _provider.GetRequiredService<IDbContextFactory<JobsDbContext>>().CreateDbContext();

    public async Task<int> CountJobsAsync()
    {
        await using var db = NewContext();
        return await db.Set<Job>().CountAsync();
    }

    public async Task<Job> SingleJobAsync()
    {
        await using var db = NewContext();
        return await db.Set<Job>().SingleAsync();
    }

    public async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition not met in time.");
            }

            await Task.Delay(20);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        if (_ownsDb && File.Exists(DbPath))
        {
            File.Delete(DbPath);
        }
    }
}
