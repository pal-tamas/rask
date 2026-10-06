using Microsoft.Extensions.Logging;

namespace Rask.Dashboard.Logging;

/// <summary>
/// A bounded, in-memory tail of the application's log — <b>not</b> a log store. It holds the last
/// <see cref="OpsOptions.LogBufferSize" /> entries at or above
/// <see cref="OpsOptions.LogMinimumLevel" /> and is gone when the process restarts; point a real
/// logging provider somewhere durable for anything you need to keep.
/// <para>
/// It exists because the failures that matter most here leave no row in any table: Litestream exiting,
/// a job type that won't deserialize, a handler that threw. Those are log lines, and the dashboard would
/// otherwise send you to the container's stdout to read them.
/// </para>
/// </summary>
public sealed class DashboardLogBuffer(OpsOptions options, TimeProvider timeProvider)
{
    private readonly Lock _gate = new();
    private readonly Queue<DashboardLogEntry> _entries = new();
    private long _sequence;

    /// <summary>Raised after an entry is added, so the log panel can push instead of poll.</summary>
    public event EventHandler? Changed;

    /// <summary>Whether this entry would be kept, checked before the message is even formatted.</summary>
    public bool IsEnabled(LogLevel level) =>
        options.CaptureLogs && level != LogLevel.None && level >= options.LogMinimumLevel;

    /// <summary>The buffered entries, newest first, optionally filtered.</summary>
    public IReadOnlyList<DashboardLogEntry> Snapshot(LogLevel? minimumLevel = null, string? category = null)
    {
        lock (_gate)
        {
            IEnumerable<DashboardLogEntry> query = _entries;

            if (minimumLevel is { } level)
            {
                query = query.Where(e => e.Level >= level);
            }

            if (!string.IsNullOrWhiteSpace(category))
            {
                query = query.Where(e => e.Category.Contains(category, StringComparison.OrdinalIgnoreCase));
            }

            return [.. query.Reverse()];
        }
    }

    /// <summary>The distinct categories currently in the buffer, for the filter dropdown.</summary>
    public IReadOnlyList<string> Categories()
    {
        lock (_gate)
        {
            return [.. _entries.Select(e => e.Category).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        }
    }

    /// <summary>Drops everything currently buffered.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void Add(LogLevel level, string category, string message, Exception? exception)
    {
        lock (_gate)
        {
            _entries.Enqueue(new DashboardLogEntry(
                ++_sequence,
                timeProvider.GetUtcNow(),
                level,
                category,
                message,
                exception?.ToString()));

            // Bounded by count: the oldest entry leaves as the newest arrives, so memory is flat no matter
            // how chatty the app gets.
            while (_entries.Count > options.LogBufferSize)
            {
                _entries.Dequeue();
            }
        }

        // Raised outside the lock: a subscriber re-rendering must never block the logging call that
        // triggered it, and re-entering Add from a handler would otherwise deadlock.
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
