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
