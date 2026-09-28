using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Rask.Logging;

/// <summary>
///     A <c>Trim</c> still being worded: <c>await Logs.Trim().OlderThan(30.Days)</c>. Nothing is removed
///     until it is awaited.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct Trimming
{
    private readonly ILogs? _logs;
    private readonly TimeSpan? _olderThan;
    private readonly int? _keepNewest;
    private readonly CancellationToken _cancellationToken;

    internal Trimming(ILogs? logs, TimeSpan? olderThan, int? keepNewest, CancellationToken cancellationToken)
    {
        _logs = logs;
        _olderThan = olderThan;
        _keepNewest = keepNewest;
        _cancellationToken = cancellationToken;
    }

    /// <summary>Drops entries older than <paramref name="age" />: <c>.OlderThan(30.Days)</c>.</summary>
    /// <param name="age">How far back to keep.</param>
    public Trimming OlderThan(TimeSpan age)
    {
        if (age <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(age), age, "OlderThan takes a positive age — to remove everything, call Logs.Clear().");
        }

        return new Trimming(_logs, age, _keepNewest, _cancellationToken);
    }

    /// <summary>Keeps at most <paramref name="rows" /> entries, newest first: <c>.KeepingNewest(100_000)</c>.</summary>
    /// <param name="rows">How many to keep.</param>
    public Trimming KeepingNewest(int rows)
    {
        if (rows < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rows), rows, "KeepingNewest takes at least 1 — to remove everything, call Logs.Clear().");
        }

        return new Trimming(_logs, _olderThan, rows, _cancellationToken);
    }

    /// <summary>Runs it, handing back how many entries were removed.</summary>
    public TaskAwaiter<int> GetAwaiter() => AsTask().GetAwaiter();

    /// <summary>Runs it, choosing whether the continuation returns to the captured context.</summary>
    public ConfiguredTaskAwaitable<int> ConfigureAwait(bool continueOnCapturedContext) =>
        AsTask().ConfigureAwait(continueOnCapturedContext);

    /// <summary>Runs it, as a <see cref="Task{TResult}" />.</summary>
    public Task<int> AsTask() =>
        (_logs ?? Logs.Resolve()).Trim(_olderThan, _keepNewest, Ambient.Or(_cancellationToken));
}
