using System.ComponentModel;

namespace Rask.Logging;

/// <summary>
/// The durable log — the queryable half of the pillar. A registered <c>ILoggerProvider</c> feeds it through
/// a bounded channel and a background writer drains that channel into <see cref="Append"/> in batches,
/// so nothing on this interface is called from a logging call site.
/// <para>
/// Read it with nothing injected through <see cref="Logs"/> — <c>Rask.Dashboard</c>'s Logs page does
/// exactly that — or inject this where there is no work in progress to reach it through.
/// </para>
/// </summary>
public interface ILogs
{
    /// <summary>
    /// Appends a batch of entries, assigning each an id. Called only by the background writer, which
    /// batches precisely so this runs once per flush rather than once per log line.
    /// </summary>
    /// <param name="records">The entries to store.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    Task Append(IReadOnlyList<LogRecord> records, CancellationToken cancellationToken = default);

    /// <summary>Reads one page of entries matching <paramref name="query"/>, newest first.</summary>
    /// <param name="query">The filter. Every property is optional.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<LogPage> Search(LogQuery query, CancellationToken cancellationToken = default);

    /// <summary>The distinct categories currently stored, ordered, for a filter dropdown.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyList<string>> Categories(CancellationToken cancellationToken = default);

    /// <summary>How many entries the store currently holds.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<long> Count(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enforces retention in one pass: drops entries older than <paramref name="olderThan"/>, then trims
    /// what is left to the newest <paramref name="keepNewest"/>. <c>null</c> skips that half. Returns how
    /// many rows were removed. The primitive under <c>Trim</c>; call <c>Trim().OlderThan(30.Days)</c> instead.
    /// </summary>
    /// <param name="olderThan">Drop anything older than this, or <c>null</c> to keep by age.</param>
    /// <param name="keepNewest">Keep at most this many rows, or <c>null</c> to keep by count.</param>
    /// <param name="cancellationToken">Cancels the trim.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    Task<int> Trim(TimeSpan? olderThan, int? keepNewest, CancellationToken cancellationToken = default);

    /// <summary>Deletes every stored entry.</summary>
    /// <param name="cancellationToken">Cancels the delete.</param>
    Task Clear(CancellationToken cancellationToken = default);
}
