using Microsoft.Extensions.Logging;
using Rask.SQLite;

namespace Rask.Logging;

/// <summary>Options for the durable log store.</summary>
public sealed class RaskLoggingOptions
{
    /// <summary>
    /// The categories never captured, whatever else is configured. The store's own plumbing must not be
    /// logged into the store: a SQLite failure that logs a line that fails to write logs a line. Matched as
    /// a prefix, so <c>Rask.Logging.LogWriter</c> is covered by <c>Rask.Logging</c>.
    /// </summary>
    private static readonly string[] AlwaysExcluded = ["Rask.Logging", "Microsoft.Data.Sqlite"];

    /// <summary>
    /// The lowest level captured. Default <see cref="LogLevel.Information" />.
    /// <para>
    /// This is a <b>floor, not an override</b>. The logging pipeline applies your <c>Logging:LogLevel</c>
    /// configuration first, so an entry filtered there never reaches the store however low this is set.
    /// </para>
    /// </summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    /// <summary>
    /// How long entries are kept. <see cref="TimeSpan.Zero"/> keeps them forever (bounded only by
    /// <see cref="MaxRows"/>). Default 14 days.
    /// </summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(14);

    /// <summary>
    /// The hard cap on stored entries — the newest <see cref="MaxRows"/> survive a sweep. <c>0</c> means no
    /// cap. Default 100,000.
    /// <para>
    /// Age alone doesn't bound the disk: a log storm can fill it well inside the retention window. This is
    /// the backstop, and the reason the defaults set both.
    /// </para>
    /// </summary>
    public int MaxRows { get; set; } = 100_000;

    /// <summary>
    /// How often the writer drains the buffer to disk. Default 1s — the coalescing window that turns a
    /// chatty second into one transaction instead of hundreds. It is also the worst-case delay before a
    /// logged line is queryable.
    /// </summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>How many entries go into one insert transaction. Default 500.</summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>
    /// How many entries the in-memory buffer holds between flushes. Default 10,000. When it is full,
    /// further entries are <b>dropped</b> and counted on <c>rask.logs.dropped</c> — logging must never
    /// become backpressure on a request.
    /// </summary>
    public int QueueCapacity { get; set; } = 10_000;

    /// <summary>How often retention is enforced. Default 1 hour.</summary>
    public TimeSpan PurgeInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How long the writer spends draining the buffer on shutdown before giving up. Default 5s. The last
    /// lines before a crash are the ones most worth keeping, so this is not zero — but it also cannot
    /// stall a host that is trying to stop.
    /// </summary>
    public TimeSpan ShutdownDrainTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Whether the ambient <c>ILogger.BeginScope</c> state — a request id, a user id, a correlation id — is
    /// stored alongside each entry. On by default: it is what lets the store answer "what else happened on
    /// that request?" instead of leaving it to be reconstructed from message text.
    /// <para>
    /// Turn it off if your scopes carry values you do not want at rest. Note the cost is paid at the log
    /// call: flattening has to happen there, because scope state is short-lived and may be reused the
    /// moment the scope closes. Only the JSON encoding is deferred to the writer's own thread.
    /// </para>
    /// </summary>
    public bool CaptureScopes { get; set; } = true;

    /// <summary>
    /// Upper bound on captured scope pairs per entry. Default 16 — deep enough for any realistic nesting,
    /// and a bound so a runaway loop of nested scopes cannot grow a row without limit.
    /// </summary>
    public int MaxScopeValues { get; set; } = 16;

    /// <summary>
    /// Upper bound on each captured scope value, in characters. Default 256, so one large object's
    /// <c>ToString()</c> cannot dominate the store.
    /// </summary>
    public int MaxScopeValueLength { get; set; } = 256;

    /// <summary>
    /// The production pragmas applied to every connection the store opens. Defaults to the same tuned set
    /// <c>Rask.SQLite</c> applies to the application database — WAL matters here in particular, since it is
    /// what lets a dashboard read the store while the writer is flushing.
    /// </summary>
    public SqliteOptions Pragmas { get; set; } = new();

    /// <summary>
    /// The non-blocking busy-retry used when the write lock is contended (a reader checkpointing, a second
    /// process). Defaults to <c>Rask.SQLite</c>'s constant-interval retry.
    /// </summary>
    public SqliteBusyRetryOptions BusyRetry { get; set; } = new();

    /// <summary>
    /// Additional logger categories to skip, matched as prefixes. A noisy category you never want on disk
    /// goes here; the store's own categories are always excluded regardless.
    /// </summary>
    public IList<string> ExcludedCategories { get; } = [];

    /// <summary>Whether <paramref name="category"/> is excluded from capture.</summary>
    internal bool IsExcluded(string category)
    {
        foreach (var prefix in AlwaysExcluded)
        {
            if (category.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        // Indexed rather than foreach: this runs per logger construction, and IList<string> would box an
        // enumerator on every call.
        for (var i = 0; i < ExcludedCategories.Count; i++)
        {
            var prefix = ExcludedCategories[i];
            if (!string.IsNullOrEmpty(prefix) && category.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Validates the option values at registration, so a bad value fails fast.</summary>
    internal void Validate()
    {
        if (Retention < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(Retention), Retention, "Retention cannot be negative.");
        }

        if (MaxRows < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxRows), MaxRows, "MaxRows cannot be negative.");
        }

        if (FlushInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(FlushInterval), FlushInterval, "FlushInterval must be positive.");
        }

        if (BatchSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(BatchSize), BatchSize, "BatchSize must be at least 1.");
        }

        if (MaxScopeValues < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxScopeValues), MaxScopeValues,
                "MaxScopeValues must be at least 1. Set CaptureScopes = false to store no scope state.");
        }

        if (MaxScopeValueLength < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxScopeValueLength), MaxScopeValueLength, "MaxScopeValueLength must be at least 1.");
        }

        if (QueueCapacity < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(QueueCapacity), QueueCapacity, "QueueCapacity must be at least 1.");
        }

        if (PurgeInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PurgeInterval), PurgeInterval, "PurgeInterval must be positive.");
        }

        if (ShutdownDrainTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ShutdownDrainTimeout), ShutdownDrainTimeout, "ShutdownDrainTimeout cannot be negative.");
        }

        ArgumentNullException.ThrowIfNull(Pragmas);
        ArgumentNullException.ThrowIfNull(BusyRetry);

        // SqliteOptions.Validate() is internal to Rask.SQLite, but BuildScript throws on the same bad
        // values — so building the script here buys the identical fail-fast without reaching for internals.
        SqlitePragmas.BuildScript(Pragmas);
    }
}
