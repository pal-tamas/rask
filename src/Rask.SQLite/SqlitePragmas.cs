using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Rask.SQLite;

/// <summary>
/// Turns a <see cref="SqlitePragmaOptions"/> into the <c>PRAGMA …;</c> batch and runs it on an open
/// connection. This is the single source of truth shared by the raw-ADO factory
/// (<see cref="IRaskSqliteConnectionFactory"/>) and the Entity Framework Core interceptor in the
/// <c>Rask.SQLite.EntityFrameworkCore</c> package.
/// </summary>
public static class SqlitePragmas
{
    /// <summary>
    /// Builds the semicolon-separated <c>PRAGMA</c> statements for <paramref name="options"/>. Any
    /// option left <see langword="null"/> is skipped. Returns an empty string when nothing is set.
    /// </summary>
    public static string BuildScript(SqlitePragmaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var sb = new StringBuilder();

        // busy_timeout FIRST, before any lock-taking pragma. On a brand-new database `journal_mode=WAL`
        // takes an exclusive lock, and two connections initialising concurrently would otherwise race
        // with busy_timeout still at 0 — one gets SQLITE_BUSY immediately, defeating the whole point.
        if (options.BusyTimeout is { } busyTimeout)
        {
            var milliseconds = (long)Math.Round(busyTimeout.TotalMilliseconds, MidpointRounding.AwayFromZero);
            Append(sb, "busy_timeout", milliseconds.ToString(CultureInfo.InvariantCulture));
        }

        if (options.ForeignKeys is { } fk)
        {
            Append(sb, "foreign_keys", fk ? "ON" : "OFF");
        }

        // journal_mode must run outside any transaction; connection-open is the one moment we can guarantee that.
        if (options.JournalMode is { } journalMode)
        {
            Append(sb, "journal_mode", JournalModeKeyword(journalMode));
        }

        if (options.Synchronous is { } synchronous)
        {
            Append(sb, "synchronous", SynchronousKeyword(synchronous));
        }

        if (options.CacheSize is { } cacheSize)
        {
            Append(sb, "cache_size", cacheSize.ToString(CultureInfo.InvariantCulture));
        }

        if (options.MmapSize is { } mmapSize)
        {
            Append(sb, "mmap_size", mmapSize.ToString(CultureInfo.InvariantCulture));
        }

        if (options.JournalSizeLimit is { } journalSizeLimit)
        {
            Append(sb, "journal_size_limit", journalSizeLimit.ToString(CultureInfo.InvariantCulture));
        }

        if (options.TempStore is { } tempStore)
        {
            Append(sb, "temp_store", TempStoreKeyword(tempStore));
        }

        return sb.ToString();
    }

    /// <summary>Executes <see cref="BuildScript"/> against an already-open <paramref name="connection"/>.</summary>
    public static void Apply(SqliteConnection connection, SqlitePragmaOptions options)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var script = BuildScript(options);
        if (script.Length == 0)
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText = script;
        command.ExecuteNonQuery();
    }

    /// <summary>Asynchronously executes <see cref="BuildScript"/> against an open <paramref name="connection"/>.</summary>
    public static async Task ApplyAsync(
        SqliteConnection connection,
        SqlitePragmaOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var script = BuildScript(options);
        if (script.Length == 0)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = script;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void Append(StringBuilder sb, string pragma, string value)
    {
        if (sb.Length > 0)
        {
            sb.Append(' ');
        }

        sb.Append("PRAGMA ").Append(pragma).Append('=').Append(value).Append(';');
    }

    private static string JournalModeKeyword(SqliteJournalMode mode) => mode switch
    {
        SqliteJournalMode.Delete => "DELETE",
        SqliteJournalMode.Truncate => "TRUNCATE",
        SqliteJournalMode.Persist => "PERSIST",
        SqliteJournalMode.Memory => "MEMORY",
        SqliteJournalMode.Wal => "WAL",
        SqliteJournalMode.Off => "OFF",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown journal mode."),
    };

    private static string SynchronousKeyword(SqliteSynchronous synchronous) => synchronous switch
    {
        SqliteSynchronous.Off => "OFF",
        SqliteSynchronous.Normal => "NORMAL",
        SqliteSynchronous.Full => "FULL",
        SqliteSynchronous.Extra => "EXTRA",
        _ => throw new ArgumentOutOfRangeException(nameof(synchronous), synchronous, "Unknown synchronous mode."),
    };

    private static string TempStoreKeyword(SqliteTempStore tempStore) => tempStore switch
    {
        SqliteTempStore.Default => "DEFAULT",
        SqliteTempStore.File => "FILE",
        SqliteTempStore.Memory => "MEMORY",
        _ => throw new ArgumentOutOfRangeException(nameof(tempStore), tempStore, "Unknown temp store."),
    };
}
