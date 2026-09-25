using Microsoft.Extensions.Logging;

namespace Rask.Dashboard.Logging;

/// <summary>One captured log entry.</summary>
/// <param name="Sequence">Monotonic id — the stable key for a list row, since timestamps can collide.</param>
/// <param name="Timestamp">When it was logged (UTC).</param>
/// <param name="Level">Its severity.</param>
/// <param name="Category">The logger category, e.g. <c>Rask.Live</c>.</param>
/// <param name="Message">The formatted message.</param>
/// <param name="Exception">The exception's <c>ToString()</c>, if one was attached.</param>
public sealed record DashboardLogEntry(
    long Sequence,
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    string Message,
    string? Exception);
