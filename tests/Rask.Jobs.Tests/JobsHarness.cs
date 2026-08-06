using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Rask.Jobs.Tests;

// ── Test jobs + handlers. ────────────────────────────────────────────────────────────────────────────────
// These are top-level purely for readability; nesting is fine now that the generator keys registrations on
// the runtime metadata name rather than a C# display string. See KeywordNamespaceJob.cs for the shape that
// used to dead-letter, and Outer.NestedJob below for the '+'-vs-'.' case.

public sealed record RecordJob(string Value) : IJob;

public sealed record FailingJob : IJob;

public sealed record TickJob : IJob;

/// <summary>
/// A job whose handler deletes the highest-numbered still-pending job row — i.e. one that is sitting in the
/// very batch currently being drained. Stands in for anything that writes to the jobs table underneath the
/// processor: a manual SQL fix, an ops dashboard's "delete", a cleanup script.
/// </summary>
public sealed record SaboteurJob : IJob;

/// <summary>A job whose handler parks until the test releases it — the lever for the shutdown-grace tests.</summary>
public sealed record GateJob : IJob;

/// <summary>
/// A job whose handler throws <see cref="OperationCanceledException"/> from its <em>own</em> reasoning, with
/// no shutdown in progress. Pins the catch-filter: only a cancellation that coincides with the host stopping
/// is an interruption; this one is an ordinary failure and must count an attempt.
/// </summary>
public sealed record SelfCancellingJob : IJob;

/// <summary>Latches for driving a handler across a shutdown: entered → (test acts) → released → completed.</summary>
public sealed class Gate
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

public sealed class GateJobHandler(Gate gate) : ICommandHandler<GateJob>
{
    public async Task HandleAsync(GateJob command, CancellationToken cancellationToken)
    {
        gate.Entered.TrySetResult();
        // Observes the token, so a grace expiry actually cancels it — a handler that ignored its token
        // could not be cancelled at all and would prove nothing about the grace period.
        await gate.Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        gate.Completed.TrySetResult();
    }
}

public sealed class SelfCancellingJobHandler : ICommandHandler<SelfCancellingJob>
{
    public Task HandleAsync(SelfCancellingJob command, CancellationToken cancellationToken) =>
        throw new OperationCanceledException("the handler gave up on its own");
}

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

public sealed class SaboteurJobHandler(IDbContextFactory<JobsDbContext> factory) : ICommandHandler<SaboteurJob>
{
    public async Task HandleAsync(SaboteurJob command, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var doomed = await db.Set<Job>()
            .Where(j => j.ProcessedAt == null)
            .OrderByDescending(j => j.Id)
            .Select(j => j.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (doomed != 0)
        {
            await db.Set<Job>().Where(j => j.Id == doomed).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }
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
        services.AddLogging(b => b.AddProvider(new CapturingLoggerProvider(Logs)));
        services.AddSingleton(Recorder);
        services.AddSingleton(Gate);
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

    /// <summary>Every formatted log message the processor wrote, for asserting on diagnostics.</summary>
    public System.Collections.Concurrent.ConcurrentBag<string> Logs { get; } = [];

    public FakeTimeProvider Clock { get; }

    public Recorder Recorder { get; } = new();

    /// <summary>Latches for <see cref="GateJob"/>, so a test can hold a handler open across a shutdown.</summary>
    public Gate Gate { get; } = new();

    public IJobQueue Queue => _provider.GetRequiredService<IJobQueue>();

    /// <summary>Resolves a service from the harness's container.</summary>
    public T Get<T>() where T : notnull => _provider.GetRequiredService<T>();

    /// <summary>Starts the processor, waits for <paramref name="until"/>, and stops it again.</summary>
    public async Task RunUntilAsync(Func<bool> until, TimeSpan? timeout = null)
    {
        await Processor.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => Task.FromResult(until()), timeout);
        }
        finally
        {
            await Processor.StopAsync(CancellationToken.None);
        }
    }

    public IHostedService Processor => Jobs;

    /// <summary>
    /// The processor, typed — so a test can drive <c>ClaimAsync</c> directly instead of racing two
    /// background services and hoping the interleaving it wants actually happens.
    /// </summary>
    public JobProcessor<JobsDbContext> Jobs =>
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

    /// <summary>
    /// How many attempts have finished. <see cref="Job.Attempts"/> counts attempts *started* — the claim
    /// increments it, so a job that kills the process still counts toward MaxAttempts — which means it goes
    /// up before the attempt is over. The lease is what says "over": the claim takes it and the outcome
    /// hands it back, so an unclaimed row has no attempt in flight.
    /// </summary>
    /// <remarks>
    /// This is the predicate to wait on before acting on an attempt's outcome — including before stopping
    /// the processor, which rolls back the claim of anything it still holds. Not keyed on
    /// <see cref="Job.Error"/>: a failure leaves it set, and the *next* claim doesn't clear it, so "Attempts
    /// went up and Error is non-null" is briefly true while attempt N+1 is still running.
    /// </remarks>
    public async Task<int> FinishedAttemptsAsync()
    {
        var job = await SingleJobAsync();
        return job.ClaimToken is null ? job.Attempts : job.Attempts - 1;
    }

    public async Task WaitUntilAsync(
        Func<Task<bool>> condition,
        TimeSpan? timeout = null,
        [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(condition))] string? expression = null)
    {
        var limit = timeout ?? TimeSpan.FromSeconds(10);
        var deadline = DateTime.UtcNow + limit;
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                // Named, because the alternative is a timeout that says nothing and an assertion twenty
                // lines later that blames the wrong thing.
                throw new TimeoutException($"`{expression}` was still false after {limit}.");
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

/// <summary>Collects formatted log messages so a test can assert on what the processor actually said.</summary>
internal sealed class CapturingLoggerProvider(System.Collections.Concurrent.ConcurrentBag<string> sink) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(sink);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(System.Collections.Concurrent.ConcurrentBag<string> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            sink.Add(formatter(state, exception));
        }
    }
}
