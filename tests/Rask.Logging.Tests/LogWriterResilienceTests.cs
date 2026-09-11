using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Rask.Logging.Tests;

/// <summary>
/// A log store that cannot be reached is an inconvenience; a log store that takes the host down with it is a
/// catastrophe. These tests drive the writer directly against a store that fails, because the failure has to
/// be survivable rather than merely unlikely.
/// </summary>
public sealed class LogWriterResilienceTests
{
    [Fact]
    public async Task AFailingStoreDoesNotFaultTheHost()
    {
        var store = new FaultyLogStore { Fail = true };
        using var writer = Build(store, out var channel, new RaskLoggingOptions
        {
            FlushInterval = TimeSpan.FromMilliseconds(20),
        });

        channel.Write(Entry("while broken"));
        await writer.StartAsync(CancellationToken.None);

        // Wait for the write to actually fail. Only one attempt happens: the batch left the buffer before
        // the store threw, so the following cycles find nothing to flush.
        await WaitUntilAsync(() => store.Attempts >= 1);

        // The store starts working again and the next tick writes normally, without anything having
        // restarted the writer. The entry buffered while it was broken is gone — a failed batch is already
        // out of the buffer, and it is counted as dropped rather than replayed.
        store.Fail = false;
        channel.Write(Entry("after recovery"));
        await WaitUntilAsync(() => store.Appended.Count > 0);

        await writer.StopAsync(CancellationToken.None);
        Assert.Equal("after recovery", Assert.Single(store.Appended).Message);
    }

    [Fact]
    public async Task AFailingShutdownDrainDoesNotThrowOutOfStop()
    {
        var store = new FaultyLogStore { Fail = true };
        using var writer = Build(store, out var channel, new RaskLoggingOptions
        {
            FlushInterval = TimeSpan.FromMinutes(5),
        });

        await writer.StartAsync(CancellationToken.None);
        channel.Write(Entry("lost"));

        Assert.Null(await Record.ExceptionAsync(() => writer.StopAsync(CancellationToken.None)));
    }

    /// <summary>
    /// A store that hangs must not hold the host open. The drain is bounded, and the entries it could not
    /// write are lost — deliberately, because a shutdown that never finishes is worse.
    /// </summary>
    [Fact]
    public async Task AHangingStoreCannotStallShutdownPastTheDrainTimeout()
    {
        var store = new FaultyLogStore { Hang = true };
        using var writer = Build(store, out var channel, new RaskLoggingOptions
        {
            FlushInterval = TimeSpan.FromMinutes(5),
            ShutdownDrainTimeout = TimeSpan.FromMilliseconds(200),
        });

        await writer.StartAsync(CancellationToken.None);
        channel.Write(Entry("never written"));

        var started = Environment.TickCount64;
        await writer.StopAsync(CancellationToken.None);

        Assert.True(
            Environment.TickCount64 - started < 5_000,
            "the shutdown drain must give up rather than wait on an unreachable store");
    }

    /// <summary>
    /// <see cref="RaskLoggingOptions.ShutdownDrainTimeout"/> governs exactly one thing: whether
    /// <c>StopAsync</c> runs a final flush. These two cases assert that, and nothing else.
    /// </summary>
    /// <remarks>
    /// The writer's own loop is deliberately never started. It is not scenery: the loop drains on its
    /// first cycle, so a started writer races the test for the same entry and a loaded machine lets the
    /// loop win — which is not a bug in the writer, since draining an entry claimed before shutdown is
    /// exactly right. Asserting on the store while both paths can reach it therefore tests the scheduler
    /// (see #594). With no loop, the drain branch is the only code that can append, so the pair below
    /// pins the option's effect in both directions with no timing at all.
    /// </remarks>
    [Fact]
    public async Task NoDrainRunsWhenTheTimeoutIsZero()
    {
        var store = new FaultyLogStore();
        using var writer = Build(store, out var channel, new RaskLoggingOptions
        {
            FlushInterval = TimeSpan.FromMinutes(5),
            ShutdownDrainTimeout = TimeSpan.Zero,
        });

        channel.Write(Entry("pending at shutdown"));
        await writer.StopAsync(CancellationToken.None);

        Assert.Empty(store.Appended);
        Assert.Equal(0, store.Attempts);
    }

    /// <inheritdoc cref="NoDrainRunsWhenTheTimeoutIsZero"/>
    [Fact]
    public async Task TheDrainRunsWhenTheTimeoutIsPositive()
    {
        var store = new FaultyLogStore();
        using var writer = Build(store, out var channel, new RaskLoggingOptions
        {
            FlushInterval = TimeSpan.FromMinutes(5),
            ShutdownDrainTimeout = TimeSpan.FromSeconds(5),
        });

        channel.Write(Entry("pending at shutdown"));
        await writer.StopAsync(CancellationToken.None);

        Assert.Equal("pending at shutdown", Assert.Single(store.Appended).Message);
    }

    /// <summary>
    /// A store that stays broken — a log table whose migration has not run, an unwritable disk — fails every flush.
    /// It is reported once, then at most once a minute, and its recovery once: an error with a stack trace every
    /// second would bury the console it is meant to warn.
    /// </summary>
    [Fact]
    public async Task AStoreThatKeepsFailingIsReportedOnceAMinuteAndItsRecoveryOnce()
    {
        var store = new FaultyLogStore { Fail = true };
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var logger = new RecordingLogger();
        using var writer = Build(
            store,
            out var channel,
            new RaskLoggingOptions { FlushInterval = TimeSpan.FromMilliseconds(20) },
            clock,
            logger);

        await writer.StartAsync(CancellationToken.None);
        try
        {
            // An entry per cycle, so every cycle has a batch to fail on.
            await FailAtLeastAsync(store, channel, attempts: 3);
            Assert.Equal(1, logger.Count(LogLevel.Error));

            // A cycle with nothing to flush still sweeps retention, and this store serves that fine — as a disk that
            // takes deletes but not inserts would. A sweep is not a write, so it must neither announce a recovery
            // nor start the run over (which would report the very next failure as a first one).
            await WaitUntilAsync(() => store.Purges >= 1);
            Assert.Equal(0, logger.Count(LogLevel.Information));

            await FailAtLeastAsync(store, channel, attempts: store.Attempts + 3);
            Assert.Equal(1, logger.Count(LogLevel.Error));

            // A minute on, the next failure is reported again — once.
            clock.Advance(LogWriter.FailureReminderInterval);
            await FailAtLeastAsync(store, channel, attempts: store.Attempts + 4);
            Assert.Equal(2, logger.Count(LogLevel.Error));

            store.Fail = false;
            channel.Write(Entry("after recovery"));
            await WaitUntilAsync(() => logger.Count(LogLevel.Information) == 1);
        }
        finally
        {
            await writer.StopAsync(CancellationToken.None);
        }

        Assert.Equal(2, logger.Count(LogLevel.Error));
        Assert.Equal(1, logger.Count(LogLevel.Information));
    }

    private static async Task FailAtLeastAsync(FaultyLogStore store, LogChannel channel, int attempts)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (store.Attempts < attempts)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"the store was attempted {store.Attempts} times, not {attempts}.");
            }

            channel.Write(Entry("while broken"));
            await Task.Delay(10);
        }
    }

    private static LogWriter Build(
        ILogs store,
        out LogChannel channel,
        RaskLoggingOptions options,
        TimeProvider? clock = null,
        ILogger<LogWriter>? logger = null)
    {
        var metrics = new LogMetrics();
        channel = new LogChannel(options, metrics);
        return new LogWriter(
            channel,
            store,
            options,
            metrics,
            clock ?? TimeProvider.System,
            logger ?? NullLogger<LogWriter>.Instance);
    }

    /// <summary>Counts what the writer reports, by level.</summary>
    private sealed class RecordingLogger : ILogger<LogWriter>
    {
        private readonly Lock _gate = new();
        private readonly List<LogLevel> _levels = [];

        public int Count(LogLevel level)
        {
            lock (_gate)
            {
                return _levels.Count(l => l == level);
            }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_gate)
            {
                _levels.Add(logLevel);
            }
        }
    }

    private static LogRecord Entry(string message) =>
        new(0, DateTimeOffset.UnixEpoch, LogLevel.Information, "Test", 0, message, null);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition not met in time.");
            }

            await Task.Delay(20);
        }
    }

    /// <summary>A store that can be told to fail, to hang, or to work.</summary>
    private sealed class FaultyLogStore : ILogs
    {
        private readonly Lock _gate = new();
        private readonly List<LogRecord> _appended = [];
        private int _attempts;
        private int _purges;

        public bool Fail { get; set; }

        public bool Hang { get; set; }

        public int Attempts => Volatile.Read(ref _attempts);

        /// <summary>How many retention sweeps reached the store. They always succeed, even while appends fail.</summary>
        public int Purges => Volatile.Read(ref _purges);

        public IReadOnlyList<LogRecord> Appended
        {
            get { lock (_gate) { return _appended.ToArray(); } }
        }

        public async Task AppendAsync(
            IReadOnlyList<LogRecord> records,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _attempts);

            if (Hang)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }

            if (Fail)
            {
                throw new InvalidOperationException("the store is unreachable");
            }

            lock (_gate)
            {
                _appended.AddRange(records);
            }
        }

        public Task<LogPage> SearchAsync(LogQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(LogPage.Empty(1, 50));

        public Task<IReadOnlyList<string>> CategoriesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<long> CountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0L);

        public Task<int> PurgeAsync(
            TimeSpan retention,
            int maxRows,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _purges);
            return Task.FromResult(0);
        }

        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
