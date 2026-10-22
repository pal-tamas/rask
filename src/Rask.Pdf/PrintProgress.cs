using System.Collections.Concurrent;
using System.Diagnostics;

namespace Rask.Printing;

/// <summary>
/// Where one print is: the step it is waiting on, and the page's requests not yet answered. A print that runs out of
/// time says which, so a stuck print names what it was waiting for instead of only that it waited.
/// </summary>
internal sealed class PrintProgress
{
    private readonly ConcurrentDictionary<string, string> _unanswered = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<(string Step, TimeSpan At)> _steps = new();
    private readonly long _started = Stopwatch.GetTimestamp();
    private volatile string _step = "waiting for its turn";

    /// <summary>The steps taken so far, each with when it began, from the print's start.</summary>
    public IReadOnlyCollection<(string Step, TimeSpan At)> Steps => _steps;

    /// <summary>How long the print has run.</summary>
    public TimeSpan Elapsed => Stopwatch.GetElapsedTime(_started);

    /// <summary>Records the step the print now waits on, worded to follow "while …".</summary>
    public void Now(string step)
    {
        _step = step;
        _steps.Enqueue((step, Elapsed));
    }

    /// <summary>A request the page made that has not been answered yet.</summary>
    public void Asked(string requestId, string url) => _unanswered[requestId] = url;

    /// <summary>The request has its answer.</summary>
    public void Answered(string requestId) => _unanswered.TryRemove(requestId, out _);

    /// <summary>"while …", and the requests still waiting for an answer, if any.</summary>
    public string Describe()
    {
        var waiting = _unanswered.Values.ToList();
        return waiting.Count == 0
            ? $"while {_step}"
            : $"while {_step}; the page's requests still unanswered: {string.Join(", ", waiting)}";
    }
}
