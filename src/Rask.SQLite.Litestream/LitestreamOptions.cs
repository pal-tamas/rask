namespace Rask.SQLite.Litestream;

/// <summary>
/// Configures the managed <a href="https://litestream.io">Litestream</a> supervisor: which database to
/// back up, where its replica lives, and how the local <c>litestream</c> binary is invoked.
/// </summary>
public sealed class LitestreamOptions
{
    /// <summary>
    /// Path to the <c>litestream</c> executable. Defaults to <c>"litestream"</c>, which resolves to the
    /// binary the package drops next to your app at build time (see <c>RaskLitestreamDownload</c>), then
    /// falls back to a <c>PATH</c> lookup. Set an absolute path to use a specific binary
    /// (e.g. <c>/usr/local/bin/litestream</c>).
    /// </summary>
    public string ExecutablePath { get; set; } = "litestream";

    /// <summary>The SQLite database file to replicate — the same <c>Data Source</c> path your app opens.</summary>
    public string? DatabasePath { get; set; }

    /// <summary>
    /// The replica URL Litestream streams to, e.g. <c>s3://bucket/db</c>, <c>gcs://bucket/db</c>,
    /// <c>abs://container/db</c>, or <c>file:///var/backups/db</c>. Required unless
    /// <see cref="ConfigPath"/> is set (which supplies databases and replicas itself).
    /// </summary>
    public string? ReplicaUrl { get; set; }

    /// <summary>
    /// An optional path to a full <c>litestream.yml</c> config file. When set, the supervisor uses
    /// <c>-config</c> and ignores <see cref="DatabasePath"/>/<see cref="ReplicaUrl"/> for replication —
    /// use this for multiple databases or advanced replica settings (retention, sync interval, …).
    /// </summary>
    public string? ConfigPath { get; set; }

    /// <summary>
    /// Whether <see cref="LitestreamStartupExtensions.RestoreSqliteFromLitestreamAsync"/> restores the
    /// database from its replica when the local file is missing (a fresh container/host). Defaults to
    /// <see langword="true"/>. Restore never overwrites an existing local database.
    /// </summary>
    public bool RestoreOnStartup { get; set; } = true;

    /// <summary>
    /// How long to let <c>litestream</c> flush its final WAL frames after an interrupt (SIGINT) before
    /// it is force-killed on shutdown. Defaults to 10 seconds. This matters on platforms that recycle
    /// the process with a SIGTERM (e.g. Azure App Service, Kubernetes) — a clean stop replicates the
    /// last writes instead of losing up to one sync interval.
    /// </summary>
    public TimeSpan ShutdownGracePeriod { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The initial delay before restarting <c>litestream replicate</c> after it exits or crashes
    /// unexpectedly. Backs off exponentially up to one minute across consecutive failures. Defaults to
    /// 5 seconds. Set to <see cref="TimeSpan.Zero"/> to retry immediately.
    /// </summary>
    public TimeSpan RestartDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The opt-in restore-verification pass: proves the replica can be read back, rather than only that
    /// the replicator is running. Off by default — every pass costs a real restore, and a real egress
    /// bill on S3/GCS/Azure. See <see cref="LitestreamVerificationOptions"/>.
    /// </summary>
    public LitestreamVerificationOptions Verification { get; set; } = new();

    /// <summary>Throws <see cref="InvalidOperationException"/> if the options are incomplete.</summary>
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Verification);
        Verification.Validate();

        if (string.IsNullOrWhiteSpace(ExecutablePath))
        {
            throw new InvalidOperationException($"{nameof(ExecutablePath)} must not be empty.");
        }

        if (ShutdownGracePeriod < TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{nameof(ShutdownGracePeriod)} must not be negative.");
        }

        // CancelAfter(TimeSpan) throws once the delay exceeds Int32.MaxValue milliseconds.
        if (ShutdownGracePeriod.TotalMilliseconds > int.MaxValue)
        {
            throw new InvalidOperationException(
                $"{nameof(ShutdownGracePeriod)} must be at most {TimeSpan.FromMilliseconds(int.MaxValue)}.");
        }

        if (RestartDelay < TimeSpan.Zero)
        {
            throw new InvalidOperationException($"{nameof(RestartDelay)} must not be negative.");
        }

        if (RestartDelay.TotalMilliseconds > int.MaxValue)
        {
            throw new InvalidOperationException(
                $"{nameof(RestartDelay)} must be at most {TimeSpan.FromMilliseconds(int.MaxValue)}.");
        }

        if (string.IsNullOrWhiteSpace(ConfigPath))
        {
            // URL mode: both the database and its replica are required.
            if (string.IsNullOrWhiteSpace(DatabasePath))
            {
                throw new InvalidOperationException(
                    $"{nameof(DatabasePath)} is required (or set {nameof(ConfigPath)} to a litestream.yml).");
            }

            if (string.IsNullOrWhiteSpace(ReplicaUrl))
            {
                throw new InvalidOperationException(
                    $"{nameof(ReplicaUrl)} is required (or set {nameof(ConfigPath)} to a litestream.yml).");
            }
        }
    }
}
