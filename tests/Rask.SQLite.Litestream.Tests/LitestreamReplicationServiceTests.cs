using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Rask.SQLite.Litestream.Tests;

public sealed class LitestreamReplicationServiceTests
{
    [Fact]
    public async Task Replication_restarts_after_the_process_exits()
    {
        // The executor "crashes" (returns non-zero) each run; the service must restart it rather than
        // giving up after the first exit. Once enough restarts have happened it blocks until shutdown.
        var executor = new CrashingExecutor(blockAfter: 3);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ILitestreamExecutor>(executor);   // wins over the default (TryAddSingleton)
        services.AddRaskSqliteLitestream(o =>
        {
            o.DatabasePath = "/tmp/rask-litestream-restart.db";
            o.ReplicaUrl = "file:///tmp/rask-litestream-restart";
            o.RestartDelay = TimeSpan.Zero;   // spin fast for the test
        });

        await using var provider = services.BuildServiceProvider();
        var service = provider.GetServices<IHostedService>().Single();

        await service.StartAsync(CancellationToken.None);
        await executor.ReachedTarget.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        Assert.True(executor.CallCount >= 3, $"expected restarts, got {executor.CallCount} call(s).");
    }

    private sealed class CrashingExecutor(int blockAfter) : ILitestreamExecutor
    {
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public int CallCount => Volatile.Read(ref _calls);

        public Task ReachedTarget => _reached.Task;

        public async Task<int> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            var n = Interlocked.Increment(ref _calls);
            if (n >= blockAfter)
            {
                _reached.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);   // hold until shutdown cancels us
            }

            return 1;   // non-zero: an unexpected exit that must trigger a restart
        }
    }
}
