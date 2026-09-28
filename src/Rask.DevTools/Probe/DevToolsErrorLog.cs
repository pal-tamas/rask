using Rask.Core.Diagnostics;

namespace Rask.DevTools.Probe;

/// <summary>
///     A bounded list of errors, the same one repeated in a row counting up rather than filling the list.
/// </summary>
/// <remarks>
///     Written from render, dispatch and diagnostics threads and read from a panel's session, so every access takes the
///     lock. An entry the render walk is still unwinding through keeps growing its path until the boundary catches it,
///     which is why entries are mutable inside the log and handed out as records.
/// </remarks>
internal sealed class DevToolsErrorLog
{
    /// <summary>How many errors a log keeps; the oldest go first.</summary>
    internal const int Capacity = 200;

    /// <summary>How much of a stack an entry keeps.</summary>
    internal const int DetailLimit = 8 * 1024;

    private readonly Lock _gate = new();
    private readonly LinkedList<Entry> _entries = new();
    private long _sequence;

    /// <summary>Raised after every change, outside the lock. Subscribers must not block.</summary>
    internal event EventHandler? Changed;

    /// <summary>Records an error and hands back its entry, so a render fault can add the components it unwinds through.</summary>
    internal Entry Record(
        DevToolsErrorKind kind, bool isWarning, string title, string message, string? detail, string? component,
        long? componentId, bool caught, bool appWide, DateTimeOffset at, DevToolsStackVerdict? verdict = null)
    {
        Entry entry;
        lock (_gate)
        {
            var last = _entries.Last?.Value;
            if (last is not null && last.Kind == kind && string.Equals(last.Title, title, StringComparison.Ordinal)
                && string.Equals(last.Message, message, StringComparison.Ordinal)
                && last.ComponentId == componentId && last.Caught == caught)
            {
                last.Count++;
                last.At = at;
                entry = last;
            }
            else
            {
                entry = new Entry(++_sequence, kind, isWarning, title, message, Bound(detail), componentId, caught, appWide, at,
                    verdict ?? DevToolsStackVerdict.None);
                if (component is not null)
                {
                    entry.InnermostFirst.Add(component);
                }

                _entries.AddLast(entry);
                if (_entries.Count > Capacity)
                {
                    _entries.RemoveFirst();
                }
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return entry;
    }

    /// <summary>Adds the component an unwinding render fault has just left, the next one out.</summary>
    internal void Enclosing(Entry entry, string component)
    {
        lock (_gate)
        {
            // Only while the entry is being built: a repeat of it already has its path.
            if (entry.Count == 1)
            {
                entry.InnermostFirst.Add(component);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The errors held, oldest first.</summary>
    internal DevToolsError[] Snapshot()
    {
        lock (_gate)
        {
            var copy = new DevToolsError[_entries.Count];
            var i = 0;
            foreach (var entry in _entries)
            {
                copy[i++] = entry.ToRecord();
            }

            return copy;
        }
    }

    /// <summary>The sequence number the next new error will take; everything below it has been recorded.</summary>
    internal long NextSequence
    {
        get
        {
            lock (_gate)
            {
                return _sequence + 1;
            }
        }
    }

    internal void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string? Bound(string? detail) =>
        detail is null || detail.Length <= DetailLimit ? detail : detail[..DetailLimit] + Environment.NewLine + "…";

    /// <summary>A diagnostic's level, as the log keeps it: warnings and errors only.</summary>
    internal static bool IsWorthListing(RaskLogLevel level) => level is RaskLogLevel.Warning or RaskLogLevel.Error;

    internal sealed class Entry(
        long sequence, DevToolsErrorKind kind, bool isWarning, string title, string message, string? detail,
        long? componentId, bool caught, bool appWide, DateTimeOffset at, DevToolsStackVerdict verdict)
    {
        internal DevToolsErrorKind Kind { get; } = kind;
        internal string Title { get; } = title;
        internal string Message { get; } = message;
        internal long? ComponentId { get; } = componentId;
        internal bool Caught { get; } = caught;
        internal List<string> InnermostFirst { get; } = [];
        internal DateTimeOffset At { get; set; } = at;
        internal int Count { get; set; } = 1;

        internal DevToolsError ToRecord()
        {
            var path = new string[InnermostFirst.Count];
            for (var i = 0; i < path.Length; i++)
            {
                path[i] = InnermostFirst[path.Length - 1 - i];
            }

            return new DevToolsError(sequence, At, Kind, isWarning, Title, Message, detail, path, ComponentId, Caught,
                appWide, Count, verdict.LikelyFrameworkBug, verdict.Frames);
        }
    }
}
