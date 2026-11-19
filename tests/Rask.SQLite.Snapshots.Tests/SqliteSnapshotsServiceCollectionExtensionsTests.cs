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
    public async Task A_databases_store_prunes_only_its_own_snapshots_in_a_shared_directory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"rask-snap-shared-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string[] app = ["app-20260101-000000000.db", "app-20260102-000000000.db"];
        string[] audit = ["app-audit-20260101-000000000.db", "app-audit-20260102-000000000.db"];
        foreach (var name in app.Concat(audit))
        {
            await File.WriteAllTextAsync(Path.Combine(directory, name), "x", TestContext.Current.CancellationToken);
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRaskSqliteSnapshots(o =>
        {
            o.DatabasePath = "/data/app.db";
            o.DestinationDirectory = directory;
        });
        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<ISqliteSnapshotStore>();

        try
        {
            await store.Prune(retain: 1, TestContext.Current.CancellationToken);
            var listed = await store.List(TestContext.Current.CancellationToken);

            Assert.All(audit, name => Assert.True(File.Exists(Path.Combine(directory, name)), name));
            Assert.Single(listed);
            Assert.StartsWith("app-2026", listed[0].Name, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
        // Refused when the options are built — at host start, or on the first resolve here — naming the section.
        var services = new ServiceCollection();
        services.AddRaskSqliteSnapshots(o => o.DatabasePath = "/data/app.db");   // no directory, no custom store
        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(
            () => provider.GetRequiredService<SqliteSnapshotOptions>());
        Assert.Contains("Rask:Snapshots", ex.Message, StringComparison.Ordinal);
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
        public Task Save(string sourceFilePath, string snapshotName, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task Prune(int retain, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
