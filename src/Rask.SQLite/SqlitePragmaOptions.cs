namespace Rask.SQLite;

/// <summary>The SQLite <c>journal_mode</c> — how the rollback/write-ahead journal is kept.</summary>
public enum SqliteJournalMode
{
    /// <summary>Delete the rollback journal at the end of each transaction (SQLite's historical default).</summary>
    Delete,

    /// <summary>Truncate the rollback journal to zero length instead of deleting it.</summary>
    Truncate,

    /// <summary>Overwrite the rollback journal header with zeroes instead of deleting it.</summary>
    Persist,

    /// <summary>Keep the rollback journal in volatile memory (no crash safety).</summary>
    Memory,

    /// <summary>Write-Ahead Logging — readers do not block the writer; the recommended production mode.</summary>
    Wal,

    /// <summary>No journal at all (no rollback, no crash safety).</summary>
    Off,
}

/// <summary>The SQLite <c>synchronous</c> setting — how aggressively writes are flushed to disk.</summary>
public enum SqliteSynchronous
{
    /// <summary>No fsync — fastest, but a crash can corrupt the database.</summary>
    Off,

    /// <summary>fsync at the critical moments only — safe under WAL and the recommended pairing with it.</summary>
    Normal,

    /// <summary>fsync on every commit — the default outside WAL; slower.</summary>
    Full,

    /// <summary>Like <see cref="Full"/> plus an extra sync of the directory containing a rollback journal.</summary>
    Extra,
}

/// <summary>The SQLite <c>temp_store</c> setting — where temporary tables and indices live.</summary>
public enum SqliteTempStore
{
    /// <summary>Use the compile-time default (usually a file).</summary>
    Default,

    /// <summary>Store temporary objects in a file.</summary>
    File,

    /// <summary>Store temporary objects in memory.</summary>
    Memory,
}

/// <summary>
/// The SQLite pragmas <see cref="IRaskSqliteConnectionFactory"/> (for raw ADO.NET) — and the Entity
/// Framework Core interceptor in the <c>Rask.SQLite.EntityFrameworkCore</c> package — apply to every
/// connection they open.
/// Every property defaults to the value a modern <b>Ruby on Rails 8</b> app runs
/// (see <a href="https://github.com/rails/rails/pull/49349">rails/rails#49349</a>); set any property to
/// <see langword="null"/> to leave that pragma unset and fall back to SQLite's own default.
/// </summary>
/// <remarks>
/// Only <see cref="JournalMode"/> (<c>journal_mode=WAL</c>) persists in the database file header — the
/// rest are <b>per connection</b> and so must be re-applied on every open, which is exactly what the
/// interceptor and factory do (pooled connections are reused, and a reused connection is a fresh open).
/// </remarks>
public sealed class SqlitePragmaOptions
{
    /// <summary><c>journal_mode</c>. Defaults to <see cref="SqliteJournalMode.Wal"/>.</summary>
    public SqliteJournalMode? JournalMode { get; set; } = SqliteJournalMode.Wal;

    /// <summary><c>synchronous</c>. Defaults to <see cref="SqliteSynchronous.Normal"/> (the safe pairing with WAL).</summary>
    public SqliteSynchronous? Synchronous { get; set; } = SqliteSynchronous.Normal;

    /// <summary><c>foreign_keys</c> enforcement. Defaults to <see langword="true"/> (SQLite leaves it off otherwise).</summary>
    public bool? ForeignKeys { get; set; } = true;

    /// <summary>
    /// <c>busy_timeout</c> — how long a connection waits on a locked database before throwing
    /// <c>SQLITE_BUSY</c>. Defaults to 5 seconds. This is the single most effective setting against
    /// spurious "database is locked" errors under concurrent writers.
    /// </summary>
    public TimeSpan? BusyTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// <c>cache_size</c> — the per-connection page cache. Follows SQLite's sign convention: a
    /// <b>positive</b> value is a number of pages, a <b>negative</b> value is kibibytes. Defaults to
    /// <c>2000</c> pages (Rails' value; ~8&#160;MB at the default 4&#160;KiB page size).
    /// </summary>
    public int? CacheSize { get; set; } = 2000;

    /// <summary><c>mmap_size</c> in bytes — memory-mapped I/O window. Defaults to 128&#160;MiB (<c>134217728</c>).</summary>
    public long? MmapSize { get; set; } = 134_217_728;

    /// <summary>
    /// <c>journal_size_limit</c> in bytes — caps how large the WAL is allowed to grow before being
    /// truncated at a checkpoint. Defaults to 64&#160;MiB (<c>67108864</c>).
    /// </summary>
    public long? JournalSizeLimit { get; set; } = 67_108_864;

    /// <summary>
    /// <c>temp_store</c>. Defaults to <see langword="null"/> (unset — SQLite's own default), matching
    /// Rails core, which does not set it. Set to <see cref="SqliteTempStore.Memory"/> as a common
    /// performance extra if your temp objects fit in RAM.
    /// </summary>
    public SqliteTempStore? TempStore { get; set; }

    /// <summary>Throws <see cref="InvalidOperationException"/> if any configured value is out of range.</summary>
    internal void Validate()
    {
        if (JournalMode is { } jm && !Enum.IsDefined(jm))
        {
            throw new InvalidOperationException($"{nameof(JournalMode)} has an invalid value: {jm}.");
        }

        if (Synchronous is { } sync && !Enum.IsDefined(sync))
        {
            throw new InvalidOperationException($"{nameof(Synchronous)} has an invalid value: {sync}.");
        }

        if (TempStore is { } temp && !Enum.IsDefined(temp))
        {
            throw new InvalidOperationException($"{nameof(TempStore)} has an invalid value: {temp}.");
        }

        if (BusyTimeout is { } timeout)
        {
            if (timeout < TimeSpan.Zero)
            {
                throw new InvalidOperationException($"{nameof(BusyTimeout)} must not be negative (was {timeout}).");
            }

            // SQLite's busy handler takes a 32-bit millisecond count; a larger value would overflow and
            // silently disable the wait entirely.
            if (timeout.TotalMilliseconds > int.MaxValue)
            {
                throw new InvalidOperationException(
                    $"{nameof(BusyTimeout)} must be at most {TimeSpan.FromMilliseconds(int.MaxValue)} (was {timeout}).");
            }
        }

        if (MmapSize is < 0)
        {
            throw new InvalidOperationException($"{nameof(MmapSize)} must not be negative (was {MmapSize}).");
        }

        if (JournalSizeLimit is < 0)
        {
            throw new InvalidOperationException($"{nameof(JournalSizeLimit)} must not be negative (was {JournalSizeLimit}).");
        }
    }
}
