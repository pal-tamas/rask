using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Rask.SQLite.Snapshots;

/// <summary>
/// Default <see cref="ISqliteSnapshotter"/>. Uses <see cref="SqliteConnection.BackupDatabase(SqliteConnection)"/>
/// (SQLite's Online Backup API) to produce a consistent copy of the live database while writers continue —
/// never an unsafe file copy — then hands it to the store and prunes.
/// </summary>
internal sealed class SqliteSnapshotter : ISqliteSnapshotter
{
    private readonly SqliteSnapshotOptions _options;
    private readonly ISqliteSnapshotStore _store;
    private readonly ILogger<SqliteSnapshotter> _logger;

    public SqliteSnapshotter(SqliteSnapshotOptions options, ISqliteSnapshotStore store, ILogger<SqliteSnapshotter> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _store = store;
        _logger = logger;
    }

    public async Task<string> SnapshotAsync(CancellationToken cancellationToken = default)
    {
        var databasePath = _options.DatabasePath
            ?? throw new InvalidOperationException($"{nameof(SqliteSnapshotOptions.DatabasePath)} is not set.");

        var stem = Path.GetFileNameWithoutExtension(databasePath);
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture);
        var snapshotName = $"{stem}-{timestamp}.db";
        var tempPath = Path.Combine(Path.GetTempPath(), $"rask-snapshot-{Guid.NewGuid():N}.db");

        try
        {
            CreateConsistentCopy(databasePath, tempPath);
            await _store.SaveAsync(tempPath, snapshotName, cancellationToken).ConfigureAwait(false);
            await _store.PruneAsync(_options.Retain, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Created SQLite snapshot {SnapshotName}.", snapshotName);
            return snapshotName;
        }
        finally
        {
            // SaveAsync normally moves the temp file away; delete it if anything failed before that.
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException)
                {
                    // Best effort — a leftover temp file is harmless.
                }
            }
        }
    }

    private void CreateConsistentCopy(string databasePath, string destinationPath)
    {
        using var source = new SqliteConnection($"Data Source={databasePath}");
        source.Open();

        // Let the backup ride out brief write locks instead of failing on a busy database.
        using (var pragma = source.CreateCommand())
        {
            var milliseconds = (long)Math.Round(_options.BusyTimeout.TotalMilliseconds, MidpointRounding.AwayFromZero);
            pragma.CommandText = $"PRAGMA busy_timeout={milliseconds.ToString(CultureInfo.InvariantCulture)};";
            pragma.ExecuteNonQuery();
        }

        using var destination = new SqliteConnection($"Data Source={destinationPath}");
        destination.Open();

        source.BackupDatabase(destination);
    }
}
