using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Rask.SQLite.Snapshots.Tests;

public sealed class SqliteSnapshotsServiceCollectionExtensionsTests
{
    [Fact]
    public void AddRaskSqliteSnapshots_registers_snapshotter_store_and_hosted_service()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRaskSqliteSnapshots(o =>
        {
            o.DatabasePath = "/data/app.db";
            o.DestinationDirectory = "/backups";
        });

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<ISqliteSnapshotter>());
        Assert.IsType<DirectorySnapshotStore>(provider.GetService<ISqliteSnapshotStore>());
        Assert.Single(services, d => d.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void AddRaskSqliteSnapshots_is_idempotent()
    {
        var services = new ServiceCollection();
        services.AddRaskSqliteSnapshots(o => { o.DatabasePath = "/a.db"; o.DestinationDirectory = "/x"; });
        services.AddRaskSqliteSnapshots(o => { o.DatabasePath = "/b.db"; o.DestinationDirectory = "/y"; });

        Assert.Single(services, d => d.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void AddRaskSqliteSnapshots_requires_destination_without_a_custom_store()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() =>
            services.AddRaskSqliteSnapshots(o => o.DatabasePath = "/data/app.db"));   // no directory, no custom store
    }

    [Fact]
    public void AddRaskSqliteSnapshots_allows_missing_directory_with_a_custom_store()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISqliteSnapshotStore>(new FakeStore());   // registered before → wins

        services.AddRaskSqliteSnapshots(o => o.DatabasePath = "/data/app.db");   // no directory: OK

        using var provider = services.BuildServiceProvider();
        Assert.IsType<FakeStore>(provider.GetService<ISqliteSnapshotStore>());
    }

    private sealed class FakeStore : ISqliteSnapshotStore
    {
        public Task SaveAsync(string sourceFilePath, string snapshotName, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task PruneAsync(int retain, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
