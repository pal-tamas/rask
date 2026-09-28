namespace Rask.SQLite;

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
