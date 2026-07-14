using Microsoft.Extensions.Logging.Abstractions;

namespace Rask.SQLite.Litestream.Tests;

public sealed class LitestreamRestorerTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-litestream-test-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task RestoreAsync_runs_restore_when_database_is_missing()
    {
        var options = new LitestreamOptions { DatabasePath = _dbPath, ReplicaUrl = "s3://bucket/app" };
        var executor = new FakeExecutor(exitCode: 0);
        var restorer = new LitestreamRestorer(options, executor, NullLogger<LitestreamRestorer>.Instance);

        var attempted = await restorer.RestoreAsync();

        Assert.True(attempted);
        Assert.Equal(["restore", "-if-replica-exists", "-o", _dbPath, "s3://bucket/app"], executor.LastArguments);
    }

    [Fact]
    public async Task RestoreAsync_skips_when_database_already_exists()
    {
        await File.WriteAllTextAsync(_dbPath, "existing");
        var options = new LitestreamOptions { DatabasePath = _dbPath, ReplicaUrl = "s3://bucket/app" };
        var executor = new FakeExecutor(exitCode: 0);
        var restorer = new LitestreamRestorer(options, executor, NullLogger<LitestreamRestorer>.Instance);

        var attempted = await restorer.RestoreAsync();

        Assert.False(attempted);
        Assert.Equal(0, executor.CallCount);
    }

    [Fact]
    public async Task RestoreAsync_skips_when_restore_on_startup_disabled()
    {
        var options = new LitestreamOptions
        {
            DatabasePath = _dbPath,
            ReplicaUrl = "s3://bucket/app",
            RestoreOnStartup = false,
        };
        var executor = new FakeExecutor(exitCode: 0);
        var restorer = new LitestreamRestorer(options, executor, NullLogger<LitestreamRestorer>.Instance);

        Assert.False(await restorer.RestoreAsync());
        Assert.Equal(0, executor.CallCount);
    }

    [Fact]
    public async Task RestoreAsync_skips_in_config_mode_without_database_path()
    {
        var options = new LitestreamOptions { ConfigPath = "/etc/litestream.yml" };
        var executor = new FakeExecutor(exitCode: 0);
        var restorer = new LitestreamRestorer(options, executor, NullLogger<LitestreamRestorer>.Instance);

        Assert.False(await restorer.RestoreAsync());
        Assert.Equal(0, executor.CallCount);
    }

    [Fact]
    public async Task RestoreAsync_restores_over_a_zero_byte_file()
    {
        await File.WriteAllBytesAsync(_dbPath, []);
        var options = new LitestreamOptions { DatabasePath = _dbPath, ReplicaUrl = "s3://bucket/app" };
        var executor = new FakeExecutor(exitCode: 0);
        var restorer = new LitestreamRestorer(options, executor, NullLogger<LitestreamRestorer>.Instance);

        Assert.True(await restorer.RestoreAsync());
        Assert.Equal(1, executor.CallCount);
    }

    [Fact]
    public async Task RestoreAsync_throws_on_nonzero_exit()
    {
        var options = new LitestreamOptions { DatabasePath = _dbPath, ReplicaUrl = "s3://bucket/app" };
        var executor = new FakeExecutor(exitCode: 1);
        var restorer = new LitestreamRestorer(options, executor, NullLogger<LitestreamRestorer>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => restorer.RestoreAsync());
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private sealed class FakeExecutor(int exitCode) : ILitestreamExecutor
    {
        public int CallCount { get; private set; }

        public IReadOnlyList<string>? LastArguments { get; private set; }

        public Task<int> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            CallCount++;
            LastArguments = arguments;
            return Task.FromResult(exitCode);
        }
    }
}
