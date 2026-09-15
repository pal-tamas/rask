using Rask.Core.Diagnostics;

namespace Rask.DevTools.Probe;

/// <summary>Where an error the Errors tab lists came from.</summary>
internal enum DevToolsErrorKind : byte
{
    /// <summary>A component's <c>Render()</c> threw.</summary>
    Render,

    /// <summary>An event handler threw.</summary>
    Handler,

    /// <summary>An async lifecycle hook faulted.</summary>
    Lifecycle,

    /// <summary>The framework reported a warning or an error through its diagnostics.</summary>
    Diagnostic,

    /// <summary>A script on the page threw, or a promise was rejected with nobody to catch it.</summary>
    Page,

    /// <summary>An island failed to mount, update or unmount.</summary>
    Island,
}

/// <summary>One error, as the Errors tab lists it.</summary>
/// <param name="Sequence">Monotonic per log; a repeat keeps its first number.</param>
/// <param name="At">When it last happened, on the machine running the app.</param>
/// <param name="Kind">Where it came from.</param>
/// <param name="IsWarning">A framework warning rather than an error.</param>
/// <param name="Title">The exception's type, or the diagnostic's category.</param>
/// <param name="Message">What went wrong, in one line or a few.</param>
/// <param name="Detail">The stack, or the exception as .NET writes it, bounded; null when there is none.</param>
/// <param name="Path">The components it happened in, outermost first; empty when no component is known.</param>
/// <param name="ComponentId">The Tree tab's id for the innermost component, when there is one.</param>
/// <param name="Caught">An error boundary took it.</param>
/// <param name="AppWide">Reported outside any page's render or handler, so it belongs to no one page.</param>
/// <param name="Count">How many times it happened in a row.</param>
/// <param name="LikelyFrameworkBug">Its stack points at Rask rather than the app, so it may be reported as a framework bug.</param>
/// <param name="ReportFrames">The frames a report may carry; see <see cref="DevToolsBugReport" />.</param>
internal sealed record DevToolsError(
    long Sequence,
    DateTimeOffset At,
    DevToolsErrorKind Kind,
    bool IsWarning,
    string Title,
    string Message,
    string? Detail,
    IReadOnlyList<string> Path,
    long? ComponentId,
    bool Caught,
    bool AppWide,
    int Count,
    bool LikelyFrameworkBug = false,
    IReadOnlyList<string>? ReportFrames = null)
{
    /// <summary>The frames a report may carry, never null.</summary>
    public IReadOnlyList<string> ReportFrames { get; init; } = ReportFrames ?? [];
}

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
    internal event Action? Changed;

    /// <summary>Records an error and hands back its entry, so a render fault can add the components it unwinds through.</summary>
    internal Entry Record(
        DevToolsErrorKind kind, bool isWarning, string title, string message, string? detail, string? component,
        long? componentId, bool caught, bool appWide, DateTimeOffset at, DevToolsStackVerdict? verdict = null)
    {
        Entry entry;
        lock (_gate)
        {
            var last = _entries.Last?.Value;
            if (last is not null && last.Kind == kind && last.Title == title && last.Message == message
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

        Changed?.Invoke();
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

        Changed?.Invoke();
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

        Changed?.Invoke();
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
