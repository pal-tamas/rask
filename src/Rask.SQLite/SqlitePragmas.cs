using System.Data;
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

        if (options.TrustedSchema is { } trustedSchema)
        {
            Append(sb, "trusted_schema", trustedSchema ? "ON" : "OFF");
        }

        if (options.CellSizeCheck is { } cellSizeCheck)
        {
            Append(sb, "cell_size_check", cellSizeCheck ? "ON" : "OFF");
        }

        if (options.AnalysisLimit is { } analysisLimit)
        {
            Append(sb, "analysis_limit", analysisLimit.ToString(CultureInfo.InvariantCulture));
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

    /// <summary>
    /// Runs <c>PRAGMA optimize</c>, refreshing the query planner's statistics for indexes whose contents
    /// have shifted since they were last analysed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// SQLite's planner chooses between indexes using the <c>sqlite_stat1</c> table, which is written by
    /// <c>ANALYZE</c> and never updated on its own. A table that was small when it was last analysed —
    /// or never analysed at all — keeps giving the planner stale numbers, and it starts choosing badly:
    /// the classic symptom is a query that was instant in development crawling in production. SQLite's
    /// own guidance is to run this before closing a long-lived connection, or periodically in a
    /// long-running process, which is what the Entity Framework Core interceptor does on connection
    /// close.
    /// </para>
    /// <para>
    /// It is cheap and self-limiting: it analyses only what looks stale, bounded by
    /// <see cref="SqlitePragmaOptions.AnalysisLimit"/>, and does nothing at all when nothing has changed.
    /// Failures are swallowed — this is an optimisation, and a connection being torn down (or a database
    /// momentarily locked by another writer) must not surface an error from it.
    /// </para>
    /// </remarks>
    /// <param name="connection">An open SQLite connection.</param>
    public static void Optimize(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (connection.State != ConnectionState.Open)
        {
            return;
        }

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA optimize;";
            command.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // Best-effort by design: see the remarks above.
        }
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
