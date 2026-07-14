using Microsoft.Extensions.DependencyInjection;

namespace Rask.SQLite.Litestream.Tests;

// Exercises the real CliWrap-backed executor through the public ILitestreamExecutor. Unix-only: it runs
// `sh`/`sleep`, which the Windows CI image doesn't provide at those names. The unit-test matrix runs on
// Linux, so this executes there; on Windows it is a trivial pass.
public sealed class CliWrapLitestreamExecutorTests
{
    private static ILitestreamExecutor CreateExecutor(string executablePath)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRaskSqliteLitestream(o =>
        {
            o.ExecutablePath = executablePath;
            o.DatabasePath = "/tmp/rask-litestream-x.db";
            o.ReplicaUrl = "file:///tmp/rask-litestream-x";
            o.ShutdownGracePeriod = TimeSpan.FromSeconds(2);
        });
        return services.BuildServiceProvider().GetRequiredService<ILitestreamExecutor>();
    }

    [Fact]
    public async Task RunAsync_returns_the_process_exit_code()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var executor = CreateExecutor("sh");

        Assert.Equal(0, await executor.RunAsync(["-c", "exit 0"], CancellationToken.None));
        Assert.Equal(3, await executor.RunAsync(["-c", "exit 3"], CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_terminates_the_process_on_cancellation()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var executor = CreateExecutor("sleep");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => executor.RunAsync(["30"], cts.Token));
    }
}
