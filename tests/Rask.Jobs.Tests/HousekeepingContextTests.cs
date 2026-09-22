using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rask.Batteries;

namespace Rask.Jobs.Tests;

/// <summary>
///     The battery polls every few seconds forever, and EF Core logs every statement at
///     <see cref="LogLevel.Information" />, so without this the console of an idle app is nothing but the
///     jobs claim query. <c>HousekeepingContextFactory</c> is source-linked into every battery; these pin
///     it once, through the jobs one, end to end on real SQLite.
/// </summary>
[Collection(JobsDbCollection.Name)]
public sealed class HousekeepingContextTests
{
    private const string CommandCategory = "Microsoft.EntityFrameworkCore.Database.Command";

    /// <summary>EF's own prefix for the message that carries the SQL.</summary>
    private const string Executed = "Executed DbCommand";

    [Fact]
    public async Task The_poll_logs_no_sql_while_the_applications_own_insert_still_does()
    {
        await using var h = new JobsHarness();
        h.Logs.Clear();

        // The application's own call, on the application's own factory — the SQL a developer turned
        // Information on to read. It has to still be here, or the assertion below proves nothing: a sink
        // that never receives an EF command log is green whatever the batteries do.
        await h.Queue.EnqueueAsync(new RecordJob("quiet"));
        Assert.Contains(h.Logs, l => l.StartsWith(Executed, StringComparison.Ordinal));

        h.Logs.Clear();

        // Waits on the recorder, not on a query: polling the table here would be the application's own
        // factory talking, and its SQL — correctly — would land in the sink and blunt the assertion.
        await h.RunUntilAsync(() => h.Recorder.Values.Contains("quiet"));

        // The job was claimed, leased, dispatched, completed and the lease handed back on shutdown. Every
        // statement that took: not one line.
        Assert.Contains("quiet", h.Recorder.Values);
        Assert.DoesNotContain(h.Logs, l => l.StartsWith(Executed, StringComparison.Ordinal));
    }

    [Fact]
    public void A_developer_who_wants_the_poll_sql_finds_it_at_debug()
    {
        var path = NewDbPath();
        var entries = new ConcurrentBag<(string Category, LogLevel Level, string Message)>();

        try
        {
            var services = new ServiceCollection();
            services.AddLogging(b => b.SetMinimumLevel(LogLevel.Debug).AddProvider(new LevelRecordingProvider(entries)));
            services.AddDbContextFactory<JobsDbContext>(o => o.UseSqlite($"Data Source={path}"));
            using var provider = services.BuildServiceProvider();
            var quiet = new HousekeepingContextFactory<JobsDbContext>(provider);
            using var db = quiet.CreateDbContext();
            db.Database.EnsureCreated();
            entries.Clear();

            _ = db.Set<Job>().Count();

            var commands = entries
                .Where(e => e.Category == CommandCategory && e.Message.StartsWith(Executed, StringComparison.Ordinal))
                .ToArray();

            // Still logged — turned down, not turned off. The whole design rests on this: an app that asks
            // for Debug on the category gets its poll SQL back, so nothing is hidden, only quiet by default.
            Assert.NotEmpty(commands);
            Assert.All(commands, e => Assert.Equal(LogLevel.Debug, e.Level));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void A_hand_written_context_factory_is_used_not_bypassed()
    {
        var path = NewDbPath();

        try
        {
            var options = new DbContextOptionsBuilder<JobsDbContext>().UseSqlite($"Data Source={path}").Options;
            var app = new CountingContextFactory(options);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IDbContextFactory<JobsDbContext>>(app);
            services.AddSingleton(options);
            using var provider = services.BuildServiceProvider();

            using var db = new HousekeepingContextFactory<JobsDbContext>(provider).CreateDbContext();

            // A factory somebody wrote is there to do something — pick a tenant's connection, stamp a
            // filter. Quieting a log is never worth silently dropping that, so this shape stays noisy.
            Assert.Equal(1, app.Created);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void Registering_the_battery_twice_still_registers_one_processor()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRaskJobs<JobsDbContext>();
        services.AddRaskJobs<JobsDbContext>();
        services.AddDbContextFactory<JobsDbContext>(o => o.UseSqlite("Data Source=:memory:"));

        using var provider = services.BuildServiceProvider();

        // The processor is registered through a factory descriptor now, and TryAddEnumerable deduplicates
        // on the implementation type it can read off that delegate. Typed wrong, this is two processors
        // racing for the same rows — the one thing a lease cannot make safe on SQLite.
        Assert.Single(provider.GetServices<IHostedService>().OfType<JobProcessor<JobsDbContext>>());
    }

    private static string NewDbPath() =>
        Path.Combine(Path.GetTempPath(), $"rask-housekeeping-test-{Guid.NewGuid():N}.db");

    private static void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>Counts what the application's own factory was asked for.</summary>
    private sealed class CountingContextFactory(DbContextOptions<JobsDbContext> options)
        : IDbContextFactory<JobsDbContext>
    {
        private int _created;

        public int Created => Volatile.Read(ref _created);

        public JobsDbContext CreateDbContext()
        {
            Interlocked.Increment(ref _created);
            return new JobsDbContext(options);
        }
    }

    /// <summary>Keeps the category and level, which is what these tests are actually about.</summary>
    private sealed class LevelRecordingProvider(ConcurrentBag<(string Category, LogLevel Level, string Message)> sink)
        : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Recording(categoryName, sink);

        public void Dispose()
        {
        }

        private sealed class Recording(
            string category,
            ConcurrentBag<(string Category, LogLevel Level, string Message)> sink) : ILogger
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
                sink.Add((category, logLevel, formatter(state, exception)));
            }
        }
    }
}
