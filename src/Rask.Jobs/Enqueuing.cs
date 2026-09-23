using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Rask.Jobs;

/// <summary>
///     An <c>Enqueue</c> still being worded: <c>await Jobs.Enqueue(job).In(24.Hours)</c>. Nothing is
///     written until it is awaited.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct Enqueuing
{
    private readonly IJobs? _jobs;
    private readonly IJob _job;
    private readonly DateTimeOffset? _runAt;
    private readonly TimeSpan? _delay;
    private readonly CancellationToken _cancellationToken;

    internal Enqueuing(IJobs? jobs, IJob job, DateTimeOffset? runAt, TimeSpan? delay, CancellationToken cancellationToken)
    {
        _jobs = jobs;
        _job = job ?? throw new ArgumentNullException(nameof(job));
        _runAt = runAt;
        _delay = delay;
        _cancellationToken = cancellationToken;
    }

    /// <summary>Runs it no earlier than <paramref name="delay"/> from now: <c>.In(24.Hours)</c>.</summary>
    public Enqueuing In(TimeSpan delay)
    {
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), delay, "In cannot be negative — a job cannot run in the past.");
        }

        return new Enqueuing(_jobs, _job, null, delay, _cancellationToken);
    }

    /// <summary>Runs it no earlier than <paramref name="moment"/>: <c>.At(midnight)</c>.</summary>
    public Enqueuing At(DateTimeOffset moment) => new(_jobs, _job, moment, null, _cancellationToken);

    /// <summary>Runs it.</summary>
    public TaskAwaiter GetAwaiter() => AsTask().GetAwaiter();

    /// <summary>Runs it, choosing whether the continuation returns to the captured context.</summary>
    public ConfiguredTaskAwaitable ConfigureAwait(bool continueOnCapturedContext) =>
        AsTask().ConfigureAwait(continueOnCapturedContext);

    /// <summary>Runs it, as a <see cref="Task"/>.</summary>
    public Task AsTask()
    {
        var jobs = _jobs ?? Jobs.Resolve();
        return jobs.Add(_job, _runAt, _delay, Ambient.Or(_cancellationToken));
    }
}
