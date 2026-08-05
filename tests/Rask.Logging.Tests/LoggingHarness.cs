using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Rask.Logging.Tests;

/// <summary>A hand-rolled fake clock (no external package): the writer's retention checks and every stored
/// timestamp read it, so tests drive time deterministically while the flush loop ticks on the real (short)
/// interval.</summary>
public sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    private long _ticks = start.UtcTicks;

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}

/// <summary>Builds a real-SQLite service provider wired for the log store, with a controllable clock.</summary>
public sealed class LoggingHarness : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    public LoggingHarness(Action<RaskLoggingOptions>? configure = null, DateTimeOffset? start = null)
    {
        // A real file, not :memory: — an in-memory database is private to a connection, and the store opens
        // one per operation. The pooling and WAL behaviour under test only exists on a file anyway.
        DbPath = Path.Combine(Path.GetTempPath(), $"rask-logs-test-{Guid.NewGuid():N}.db");
        Clock = new FakeTimeProvider(start ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(Clock); // registered first so AddRaskLogging's TryAddSingleton keeps it
        services.AddRaskLogging($"Data Source={DbPath}", o =>
        {
            o.FlushInterval = TimeSpan.FromMilliseconds(20);
            configure?.Invoke(o);
        });

        // After AddRaskLogging, so the store's own ILoggerProvider is in the collection when the factory is
        // built — this is what makes the tests exercise the real pipeline rather than the provider directly.
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace));

        _provider = services.BuildServiceProvider();
    }

    public string DbPath { get; }

    public FakeTimeProvider Clock { get; }

    public ILogStore Store => _provider.GetRequiredService<ILogStore>();

    public ILoggerFactory LoggerFactory => _provider.GetRequiredService<ILoggerFactory>();

    /// <summary>Resolves a service from the harness's container.</summary>
    public T Get<T>() where T : notnull => _provider.GetRequiredService<T>();

    public IHostedService Writer => _provider.GetServices<IHostedService>().Single();

    /// <summary>Logs through the real pipeline, exactly as application code would.</summary>
    public ILogger Logger(string category = "Test.Category") => LoggerFactory.CreateLogger(category);

    /// <summary>Starts the writer, waits for <paramref name="until"/>, and stops it again.</summary>
    public async Task RunUntilAsync(Func<Task<bool>> until, TimeSpan? timeout = null)
    {
        await Writer.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(until, timeout);
        }
        finally
        {
            await Writer.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>Runs the writer until the store holds at least <paramref name="count"/> entries.</summary>
    public Task RunUntilStoredAsync(int count, TimeSpan? timeout = null) =>
        RunUntilAsync(async () => await Store.CountAsync() >= count, timeout);

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

        // The store's connections are pooled by connection string; the file stays locked until they are
        // released, and on Windows a delete would otherwise fail.
        Microsoft.Data.Sqlite.SqliteConnection.ClearPool(
            new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={DbPath}"));

        foreach (var path in new[] { DbPath, $"{DbPath}-wal", $"{DbPath}-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
