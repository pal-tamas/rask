namespace Rask.Logging;

/// <summary>
/// The durable log — the queryable half of the pillar. A registered <c>ILoggerProvider</c> feeds it through
/// a bounded channel and a background writer drains that channel into <see cref="AppendAsync"/> in batches,
/// so nothing on this interface is called from a logging call site.
/// <para>
/// Read it from an operator UI (<c>Rask.Dashboard</c>'s Logs page does exactly that) or from your own code.
/// </para>
/// </summary>
public interface ILogStore
{
    /// <summary>
    /// Appends a batch of entries, assigning each an id. Called only by the background writer, which
    /// batches precisely so this runs once per flush rather than once per log line.
    /// </summary>
    Task AppendAsync(IReadOnlyList<LogRecord> records, CancellationToken cancellationToken = default);

    /// <summary>Reads one page of entries matching <paramref name="query"/>, newest first.</summary>
    Task<LogPage> QueryAsync(LogQuery query, CancellationToken cancellationToken = default);

    /// <summary>The distinct categories currently stored, ordered, for a filter dropdown.</summary>
    Task<IReadOnlyList<string>> CategoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>How many entries the store currently holds.</summary>
    Task<long> CountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enforces retention: drops entries older than <paramref name="retention"/>, then trims the store to
    /// the newest <paramref name="maxRows"/>. <see cref="TimeSpan.Zero"/> and <c>0</c> respectively disable
    /// each half. Returns how many rows were removed.
    /// </summary>
    Task<int> PurgeAsync(TimeSpan retention, int maxRows, CancellationToken cancellationToken = default);

    /// <summary>Deletes every stored entry.</summary>
    Task ClearAsync(CancellationToken cancellationToken = default);
}
