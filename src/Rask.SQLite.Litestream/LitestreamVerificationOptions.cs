namespace Rask.SQLite.Litestream;

/// <summary>
/// Configures the periodic <b>restore verification</b> pass — the check that proves the replica is not
/// merely being written to, but can be read back.
/// <para>
/// <b>Off by default, and deliberately so.</b> Each pass runs a real <c>litestream restore</c>, which on
/// S3/GCS/Azure means a real download and a real egress bill. Turn it on, leave
/// <see cref="Interval"/> conservative, and treat it as a scheduled audit rather than a health poll.
/// </para>
/// </summary>
public sealed class LitestreamVerificationOptions
{
    /// <summary>
    /// Whether the background verification service runs. Defaults to <see langword="false"/> — see the
    /// egress note on this class. <see cref="ISqliteBackupVerifier"/> is registered either way, so an
    /// operator endpoint can run a pass on demand without scheduling one.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// How often the background service verifies. Defaults to 24 hours. Every pass costs one restore's
    /// worth of egress, so this is a daily audit, not a liveness probe — liveness is
    /// <see cref="LitestreamReplicationStatus.IsReplicating"/>, which is free.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Whether to verify once at startup, before the first <see cref="Interval"/> elapses. Defaults to
    /// <see langword="false"/>: a fresh process has usually just restored, and a boot-time restore of the
    /// whole database is the slowest possible way to start.
    /// </summary>
    public bool VerifyOnStartup { get; set; }

    /// <summary>
    /// How long to wait after writing the sentinel before the first restore attempt, giving replication
    /// time to ship it. Defaults to 10 seconds. Sized so the common case costs exactly one restore
    /// rather than a failed attempt followed by a retry.
    /// </summary>
    public TimeSpan ReplicationGrace { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long to wait between restore attempts once the sentinel has not arrived. Defaults to 15
    /// seconds. <b>Every attempt is another restore, and another download</b>, so keep it coarse.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The total budget for one verification pass, measured from the sentinel write. Defaults to 2
    /// minutes. Exhausting it reports <see cref="LitestreamVerificationOutcome.Inconclusive"/> — "the
    /// sentinel had not shipped yet" — never a failure: replication lag is not a broken backup, and
    /// paging someone for it is how a verification job gets turned off.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Where the restored copy is written. Defaults to the system temp directory. Never beside the live
    /// database: a stray <c>-wal</c>/<c>-shm</c> next to the real file is a hazard, and the whole
    /// directory is deleted after every pass.
    /// </summary>
    public string? TempDirectory { get; set; }

    /// <summary>
    /// The busy-retry governing the sentinel write, which takes the write lock on the live database.
    /// Defaults to a 30-second budget — longer than the general-purpose default, because a verification
    /// pass can afford to wait out a busy writer and must not fail the backup report over contention.
    /// The wait is non-blocking (it yields the thread), so waiting costs nothing but time.
    /// </summary>
    public SqliteBusyRetryOptions BusyRetry { get; set; } = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>Throws <see cref="InvalidOperationException"/> if any configured value is out of range.</summary>
    internal void Validate()
    {
        if (Interval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{nameof(Interval)} must be positive (was {Interval}).");
        }

        if (ReplicationGrace < TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{nameof(ReplicationGrace)} must not be negative (was {ReplicationGrace}).");
        }

        if (PollInterval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{nameof(PollInterval)} must be positive (was {PollInterval}).");
        }

        if (Timeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{nameof(Timeout)} must be positive (was {Timeout}).");
        }

        // The grace alone eating the whole budget means no restore is ever attempted and every pass is
        // inconclusive — a verification job that silently never verifies is worse than none.
        if (ReplicationGrace >= Timeout)
        {
            throw new InvalidOperationException(
                $"{nameof(ReplicationGrace)} ({ReplicationGrace}) must be shorter than {nameof(Timeout)} ({Timeout}), "
                + "or no restore attempt fits inside the budget.");
        }

        ArgumentNullException.ThrowIfNull(BusyRetry);
    }
}
