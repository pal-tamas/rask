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

    private static LogWriter Build(ILogs store, out LogChannel channel, RaskLoggingOptions options)
    {
        var metrics = new LogMetrics();
        channel = new LogChannel(options, metrics);
        return new LogWriter(
            channel,
            store,
            options,
            metrics,
            TimeProvider.System,
            NullLogger<LogWriter>.Instance);
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

        public bool Fail { get; set; }

        public bool Hang { get; set; }

        public int Attempts => Volatile.Read(ref _attempts);

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
            CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
