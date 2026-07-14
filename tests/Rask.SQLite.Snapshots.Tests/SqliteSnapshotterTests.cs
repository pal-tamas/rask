using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.SQLite.Snapshots.Tests;

// Real Online-Backup-API round-trips against a live SQLite database file, exercised through the public
// ISqliteSnapshotter + the default DirectorySnapshotStore.
public sealed class SqliteSnapshotterTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-snap-db-{Guid.NewGuid():N}.db");
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"rask-snap-out-{Guid.NewGuid():N}");

    private ISqliteSnapshotter BuildSnapshotter(int retain)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRaskSqliteSnapshots(o =>
        {
            o.DatabasePath = _dbPath;
            o.DestinationDirectory = _dir;
            o.Retain = retain;
        });
        return services.BuildServiceProvider().GetRequiredService<ISqliteSnapshotter>();
    }

    private void SeedDatabase(int rows)
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE items(id INTEGER PRIMARY KEY, name TEXT);";
        command.ExecuteNonQuery();
        for (var i = 1; i <= rows; i++)
        {
            command.CommandText = $"INSERT INTO items(name) VALUES('item {i}');";
            command.ExecuteNonQuery();
        }
    }

    [Fact]
    public async Task SnapshotAsync_produces_a_consistent_copy_of_the_data()
    {
        SeedDatabase(rows: 5);
        var snapshotter = BuildSnapshotter(retain: 3);

        var name = await snapshotter.SnapshotAsync();

        var snapshotPath = Path.Combine(_dir, name);
        Assert.True(File.Exists(snapshotPath));

        using var connection = new SqliteConnection($"Data Source={snapshotPath};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM items;";
        Assert.Equal(5L, (long)command.ExecuteScalar()!);
    }

    [Fact]
    public async Task SnapshotAsync_prunes_to_the_retain_count()
    {
        SeedDatabase(rows: 1);
        var snapshotter = BuildSnapshotter(retain: 2);

        for (var i = 0; i < 4; i++)
        {
            await snapshotter.SnapshotAsync();
            await Task.Delay(5);   // distinct millisecond-stamped filenames
        }

        var kept = Directory.GetFiles(_dir, $"{Path.GetFileNameWithoutExtension(_dbPath)}-*.db");
        Assert.Equal(2, kept.Length);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, $"{_dbPath}-shm", $"{_dbPath}-wal" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
