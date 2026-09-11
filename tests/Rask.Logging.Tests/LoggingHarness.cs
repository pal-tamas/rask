using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
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

/// <summary>Where the harness keeps the log: the two <see cref="ILogs"/> implementations a contract runs against.</summary>
public enum LogStoreKind
{
    /// <summary><c>AddRaskLogging()</c>: a SQLite file of its own, at <c>Rask:ConnectionStrings:Logs</c>.</summary>
    File,

    /// <summary><c>AddRaskLogging&lt;TContext&gt;()</c>: the RaskLog table in the application's database.</summary>
    DbContext,
}

/// <summary>Something an application writes in its own transaction, for the tests that roll one back.</summary>
public sealed class Widget
{
    public int Id { get; set; }

    public string Name { get; set; } = "";
}

/// <summary>An application context that maps the log table beside a table of its own.</summary>
public sealed class LogTestDbContext(DbContextOptions<LogTestDbContext> options) : DbContext(options)
{
    public DbSet<Widget> Widgets => Set<Widget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddRaskLogging();
}

/// <summary>Builds a real-SQLite service provider wired for the log store, with a controllable clock.</summary>
public sealed class LoggingHarness : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    public LoggingHarness(
        Action<RaskLoggingOptions>? configure = null,
        DateTimeOffset? start = null,
        LogStoreKind kind = LogStoreKind.File)
    {
        // A real file, not :memory: — an in-memory database is private to a connection, and the store opens
        // one per operation. The pooling and WAL behaviour under test only exists on a file anyway.
        DbPath = Path.Combine(Path.GetTempPath(), $"rask-logs-test-{Guid.NewGuid():N}.db");
        Clock = new FakeTimeProvider(start ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        void Options(RaskLoggingOptions o)
        {
            o.FlushInterval = TimeSpan.FromMilliseconds(20);
            configure?.Invoke(o);
        }

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(Clock); // registered first so AddRaskLogging's TryAddSingleton keeps it

        if (kind == LogStoreKind.File)
        {
            services.AddRaskLoggingAt(ConnectionString, Options);
        }
        else
        {
            // Created on a context built outside the container, so the DDL's own EF Core log lines never reach
            // the store under test and every count a test asserts is the test's own.
            using (var db = new LogTestDbContext(
                       new DbContextOptionsBuilder<LogTestDbContext>().UseSqlite(ConnectionString).Options))
            {
                db.Database.EnsureCreated();
            }

            services.AddDbContextFactory<LogTestDbContext>(o => o.UseSqlite(ConnectionString));
            services.AddRaskLogging<LogTestDbContext>(Options);
        }

        // After AddRaskLogging, so the store's own ILoggerProvider is in the collection when the factory is
        // built — this is what makes the tests exercise the real pipeline rather than the provider directly.
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace));

        _provider = services.BuildServiceProvider();
    }

    public string DbPath { get; }

    public string ConnectionString => $"Data Source={DbPath}";

    public FakeTimeProvider Clock { get; }

    public ILogs Store => _provider.GetRequiredService<ILogs>();

    public ILoggerFactory LoggerFactory => _provider.GetRequiredService<ILoggerFactory>();

    /// <summary>Resolves a service from the harness's container.</summary>
    public T Get<T>() where T : notnull => _provider.GetRequiredService<T>();

    /// <summary>The background writer — not the application-database store's boot check, which is hosted too.</summary>
    public IHostedService Writer => _provider.GetServices<IHostedService>().OfType<LogWriter>().Single();

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

    /// <summary>
    ///     Polls <paramref name="condition"/> until it holds. The timeout names the predicate it gave up on
    ///     (the principle #589 landed in <c>JobsHarness</c>): "Condition not met in time" tells you a wait
    ///     expired and nothing about which one, on a harness whose callers all wait for different things.
    /// </summary>
    public async Task WaitUntilAsync(
        Func<Task<bool>> condition,
        TimeSpan? timeout = null,
        [CallerArgumentExpression(nameof(condition))] string? description = null)
    {
        var budget = timeout ?? TimeSpan.FromSeconds(10);
        var deadline = DateTime.UtcNow + budget;
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"`{description}` was still false after {budget}.");
            }

            await Task.Delay(20);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();

        // The store's connections are pooled by connection string; the file stays locked until they are
        // released, and on Windows a delete would otherwise fail.
        Microsoft.Data.Sqlite.SqliteConnection.ClearPool(new Microsoft.Data.Sqlite.SqliteConnection(ConnectionString));

        foreach (var path in new[] { DbPath, $"{DbPath}-wal", $"{DbPath}-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
