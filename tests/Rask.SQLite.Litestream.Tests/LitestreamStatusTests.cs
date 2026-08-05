using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Rask.SQLite.Litestream.Tests;

/// <summary>
/// "Is my backup actually running?" has to be answerable from code, not only from reading the log. The
/// supervisor publishes its state into <see cref="LitestreamStatus" /> so an operator surface can read it.
/// </summary>
public sealed class LitestreamStatusTests
{
    [Fact]
    public void Before_the_supervisor_starts_it_reports_not_replicating()
    {
        var status = new LitestreamStatus().Current;

        Assert.False(status.IsReplicating);
        Assert.Null(status.LastStartedAt);
        Assert.Null(status.LastExitedAt);
        Assert.Equal(0, status.RestartCount);
        Assert.Null(status.LastExitCode);
        Assert.Null(status.LastError);
    }

    [Fact]
    public async Task A_running_replication_reports_replicating()
    {
        var executor = new BlockingExecutor();
        await using var provider = Build(executor, out var status);
        var service = provider.GetServices<IHostedService>().Single();

        await service.StartAsync(CancellationToken.None);
        try
        {
            await executor.Started.WaitAsync(TimeSpan.FromSeconds(5));

            var current = status.Current;
            Assert.True(current.IsReplicating);
            Assert.NotNull(current.LastStartedAt);
            Assert.Equal(0, current.RestartCount);   // still on its first run
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        // Shutdown is not a backup failure: it must not be counted as a restart.
        Assert.False(status.Current.IsReplicating);
        Assert.Equal(0, status.Current.RestartCount);
    }

    [Fact]
    public async Task An_unexpected_exit_records_the_exit_code_and_counts_a_restart()
    {
        var executor = new CrashingExecutor(blockAfter: 3);
        await using var provider = Build(executor, out var status);
        var service = provider.GetServices<IHostedService>().Single();

        await service.StartAsync(CancellationToken.None);
        try
        {
            await executor.ReachedTarget.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        var current = status.Current;
        Assert.Equal(7, current.LastExitCode);
        Assert.Null(current.LastError);          // it exited, it did not fail to launch
        Assert.NotNull(current.LastExitedAt);

        // Each crash-and-restart is counted, so a flapping backup is visible as a climbing number.
        Assert.True(current.RestartCount >= 2, $"expected restarts to be counted, got {current.RestartCount}.");
    }

    [Fact]
    public async Task An_executor_that_cannot_launch_records_the_error()
    {
        var executor = new ThrowingExecutor();
        await using var provider = Build(executor, out var status);
        var service = provider.GetServices<IHostedService>().Single();

        await service.StartAsync(CancellationToken.None);
        try
        {
            await executor.Threw.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        var current = status.Current;
        Assert.False(current.IsReplicating);
        Assert.Null(current.LastExitCode);       // never got far enough to have one
        Assert.Equal("litestream is not installed", current.LastError);
    }

    private static ServiceProvider Build(ILitestreamExecutor executor, out LitestreamStatus status)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(executor);   // wins over the default (TryAddSingleton)
        services.AddRaskSqliteLitestream(o =>
        {
            o.DatabasePath = "/tmp/rask-litestream-status.db";
            o.ReplicaUrl = "file:///tmp/rask-litestream-status";
            o.RestartDelay = TimeSpan.Zero;   // spin fast for the test
        });

        var provider = services.BuildServiceProvider();
        status = provider.GetRequiredService<LitestreamStatus>();
        return provider;
    }

    private sealed class BlockingExecutor : ILitestreamExecutor
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public async Task<int> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);   // replicate runs until cancelled
            return 0;
        }
    }

    private sealed class CrashingExecutor(int blockAfter) : ILitestreamExecutor
    {
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public Task ReachedTarget => _reached.Task;

        public async Task<int> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            var n = Interlocked.Increment(ref _calls);
            if (n >= blockAfter)
            {
                _reached.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return 7;
        }
    }

    private sealed class ThrowingExecutor : ILitestreamExecutor
    {
        private readonly TaskCompletionSource _threw = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Threw => _threw.Task;

        public Task<int> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            _threw.TrySetResult();
            throw new InvalidOperationException("litestream is not installed");
        }
    }
}
