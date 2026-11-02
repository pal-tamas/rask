namespace Rask.Cli.Commands;

/// <summary>
/// How a deploy waits for something on the box to come true — a container running, an app answering, the proxy taking a
/// reload, a route checking out: asked at once, then every <see cref="Interval"/> until <see cref="Timeout"/> has passed
/// (or <see cref="MaxAttempts"/> asks). Never a wait before the first ask, so a thing that is already true costs one ask.
/// </summary>
internal sealed class ReadinessPoll
{
    /// <summary>The pause between two asks.</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>How long to keep asking, measured from the first ask on <see cref="Clock"/>.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>An upper bound on asks besides the deadline — what tests turn down to one.</summary>
    public int MaxAttempts { get; init; } = int.MaxValue;

    /// <summary>The clock the deadline is measured on.</summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>How the pause is spent — a fake in tests, so a test advances its clock instead of sleeping.</summary>
    public Func<TimeSpan, CancellationToken, Task> Sleep { get; init; } = Task.Delay;

    /// <summary>Ask <paramref name="probe"/> until it says yes (<c>true</c>) or the deadline passes (<c>false</c>).</summary>
    public async Task<bool> UntilAsync(Func<Task<bool>> probe, CancellationToken cancellationToken)
    {
        var started = Clock.GetTimestamp();
        var attempt = 1;
        while (!await probe().ConfigureAwait(false))
        {
            // A pause that would end past the deadline is not taken: the wait is over, and saying so now beats a sleep.
            if (attempt >= MaxAttempts || Clock.GetElapsedTime(started) + Interval > Timeout)
            {
                return false;
            }

            if (Interval > TimeSpan.Zero)
            {
                await Sleep(Interval, cancellationToken).ConfigureAwait(false);
            }

            attempt++;
        }

        return true;
    }
}
