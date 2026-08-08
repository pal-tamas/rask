using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Rask.Core.Browser;
using Rask.SQLite.Snapshots;

namespace Rask.SQLite.Browser.Tests;

public class RegistrationTests
{
    private static ServiceCollection Collection()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IIndexedDb>(new FakeIndexedDb());
        services.AddSingleton<IWebLocks>(new FakeWebLocks());
        return services;
    }

    [Fact]
    public void AddRaskBrowserSqlite_RegistersTheSnapshotterOverIndexedDb()
    {
        var provider = Collection().AddRaskBrowserSqlite("app").BuildServiceProvider();

        Assert.IsType<IndexedDbSnapshotStore>(provider.GetRequiredService<ISqliteSnapshotStore>());
        Assert.NotNull(provider.GetRequiredService<ISqliteSnapshotter>());
    }

    // Order is the whole contract: the host restores inside StartAsync so that anything registered after
    // it — a DbContext consumer, a job processor — opens a database that is already populated, and the
    // snapshot loop cannot tick before the restore finished.
    [Fact]
    public void AddRaskBrowserSqlite_RegistersTheHostBeforeTheSnapshotLoop()
    {
        var provider = Collection().AddRaskBrowserSqlite("app").BuildServiceProvider();

        var hosted = provider.GetServices<IHostedService>().ToArray();

        Assert.Collection(
            hosted,
            first => Assert.IsType<BrowserSqliteHost>(first),
            second => Assert.IsType<BrowserSqliteSnapshotService>(second));
    }

    // The host is resolvable on its own AND as a hosted service, and must be the same instance —
    // the snapshot loop reads IsOwner off it.
    [Fact]
    public void AddRaskBrowserSqlite_SharesOneHostInstance()
    {
        var provider = Collection().AddRaskBrowserSqlite("app").BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredService<BrowserSqliteHost>(),
            provider.GetServices<IHostedService>().OfType<BrowserSqliteHost>().Single());
    }

    // A second registration of the same database would elect two owners in one tab and snapshot twice
    // per tick.
    [Fact]
    public void AddRaskBrowserSqlite_IsIdempotentPerDatabase()
    {
        var provider = Collection()
            .AddRaskBrowserSqlite("app")
            .AddRaskBrowserSqlite("app")
            .BuildServiceProvider();

        Assert.Equal(2, provider.GetServices<IHostedService>().Count());
    }

    [Fact]
    public void AddRaskBrowserSqlite_AppliesTheConfiguredOptions()
    {
        var provider = Collection()
            .AddRaskBrowserSqlite("jobs", o => { o.SnapshotInterval = TimeSpan.FromSeconds(5); o.Retain = 3; })
            .BuildServiceProvider();

        var options = provider.GetRequiredService<BrowserSqliteOptions>();

        Assert.Equal("jobs", options.Name);
        Assert.Equal("/rask/jobs.db", options.DatabasePath);
        Assert.Equal(TimeSpan.FromSeconds(5), options.SnapshotInterval);

        // The snapshotter reads its own options type; they must agree or retention silently differs.
        var snapshotOptions = provider.GetRequiredService<SqliteSnapshotOptions>();
        Assert.Equal("/rask/jobs.db", snapshotOptions.DatabasePath);
        Assert.Equal(3, snapshotOptions.Retain);
    }

    [Fact]
    public void AddRaskBrowserSqlite_RejectsInvalidOptionsAtRegistration()
    {
        // At registration, not at first snapshot — a bad interval should fail where it was written.
        Assert.Throws<InvalidOperationException>(
            () => Collection().AddRaskBrowserSqlite("app", o => o.SnapshotInterval = TimeSpan.Zero));
    }
}

public class BrowserSqliteSnapshotServiceTests
{
    // A tab that does not own the database has nothing to persist, and snapshotting from it would be
    // exactly the overwrite the ownership lock exists to prevent.
    [Fact]
    public async Task NonOwner_NeverSnapshots()
    {
        // A real temp path: /rask exists in the WASM runtime's in-memory filesystem, not on a test machine.
        var temp = Directory.CreateTempSubdirectory("rask-snapshot-service");
        var options = new BrowserSqliteOptions
        {
            Name = "app",
            SnapshotInterval = TimeSpan.FromMilliseconds(5),
            DatabasePath = Path.Combine(temp.FullName, "app.db"),
        };
        options.Validate();

        var locks = new FakeWebLocks();
        locks.HoldElsewhere(BrowserSqlite.OwnerLockName(options.Name));

        var snapshotter = new RecordingSnapshotter();
        var host = new BrowserSqliteHost(
            options, locks, new FakeIndexedDb(), snapshotter, NullLogger<BrowserSqliteHost>.Instance);
        await host.StartAsync(CancellationToken.None);

        var service = new BrowserSqliteSnapshotService(
            options, host, snapshotter, NullLogger<BrowserSqliteSnapshotService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!;

        Assert.False(host.IsOwner);
        Assert.Equal(0, snapshotter.Count);

        temp.Delete(recursive: true);
    }
}
