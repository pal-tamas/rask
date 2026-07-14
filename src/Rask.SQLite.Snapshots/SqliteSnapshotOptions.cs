namespace Rask.SQLite.Snapshots;

/// <summary>
/// Configures scheduled SQLite snapshots: which database to back up, how often, how many to keep, and
/// (for the default directory store) where to write them.
/// </summary>
public sealed class SqliteSnapshotOptions
{
    /// <summary>The SQLite database file to snapshot — the same <c>Data Source</c> path your app opens.</summary>
    public string? DatabasePath { get; set; }

    /// <summary>
    /// The directory the default store writes snapshots to. Required unless you register your own
    /// <see cref="ISqliteSnapshotStore"/> (e.g. an object-storage implementation). Created if missing.
    /// </summary>
    public string? DestinationDirectory { get; set; }

    /// <summary>How often to take a snapshot. Defaults to 6 hours. Must be positive.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>How many of the most recent snapshots to keep; older ones are pruned. Defaults to 7. Must be at least 1.</summary>
    public int Retain { get; set; } = 7;

    /// <summary>Whether to take one snapshot immediately at startup rather than waiting a full interval. Defaults to <see langword="false"/>.</summary>
    public bool SnapshotOnStartup { get; set; }

    /// <summary>
    /// How long the backup connection waits on a locked database before giving up. Defaults to 30
    /// seconds — the Online Backup API copies pages while writers continue, and a busy timeout lets it
    /// ride out brief write locks instead of failing.
    /// </summary>
    public TimeSpan BusyTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Throws <see cref="InvalidOperationException"/> if the options are incomplete or out of range.</summary>
    internal void Validate(bool requireDestinationDirectory)
    {
        if (string.IsNullOrWhiteSpace(DatabasePath))
        {
            throw new InvalidOperationException($"{nameof(DatabasePath)} is required.");
        }

        if (requireDestinationDirectory && string.IsNullOrWhiteSpace(DestinationDirectory))
        {
            throw new InvalidOperationException(
                $"{nameof(DestinationDirectory)} is required unless a custom {nameof(ISqliteSnapshotStore)} is registered.");
        }

        if (Interval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{nameof(Interval)} must be positive (was {Interval}).");
        }

        if (Retain < 1)
        {
            throw new InvalidOperationException($"{nameof(Retain)} must be at least 1 (was {Retain}).");
        }

        if (BusyTimeout < TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{nameof(BusyTimeout)} must not be negative (was {BusyTimeout}).");
        }

        if (BusyTimeout.TotalMilliseconds > int.MaxValue)
        {
            throw new InvalidOperationException(
                $"{nameof(BusyTimeout)} must be at most {TimeSpan.FromMilliseconds(int.MaxValue)}.");
        }
    }
}
