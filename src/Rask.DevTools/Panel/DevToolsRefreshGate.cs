namespace Rask.DevTools.Panel;

/// <summary>
///     Turns a burst of feed notifications into at most one refresh per <see cref="Interval" />.
/// </summary>
/// <remarks>
///     <para>
///         A feed notifies once per frame, from the inspected session's own render and dispatch paths. Re-rendering the
///         panel for each one would send the panel a frame for every frame the page exchanges, so a busy page would pay
///         for its inspector twice. Instead the first notification schedules a refresh after the interval and the rest
///         fold into it; the refresh reads the feed as it is by then, so nothing is lost.
///     </para>
///     <para>
///         Notifying costs one compare-and-swap on the inspected session's thread and never waits: the refresh runs on
///         the thread pool and serializes on the panel's own session.
///     </para>
/// </remarks>
internal sealed class DevToolsRefreshGate
{
    /// <summary>The longest a panel lags behind the page it inspects.</summary>
    internal static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(200);

    private readonly Action _refresh;
    private readonly CancellationToken _lifetime;
    private readonly Func<CancellationToken, Task> _delay;
    private int _scheduled;

    /// <param name="refresh">What runs once the interval has passed.</param>
    /// <param name="lifetime">Cancelled when the panel goes away; no refresh runs after that.</param>
    /// <param name="delay">The wait, replaceable so a test decides when the interval ends.</param>
    internal DevToolsRefreshGate(Action refresh, CancellationToken lifetime, Func<CancellationToken, Task>? delay = null)
    {
        _refresh = refresh;
        _lifetime = lifetime;
        _delay = delay ?? (ct => Task.Delay(Interval, ct));
    }

    /// <summary>
    ///     The refresh scheduled most recently, complete once it has run or been dropped. Nothing waits on it here; it is
    ///     what a test awaits instead of guessing how long the thread pool takes to run it.
    /// </summary>
    internal Task Scheduled { get; private set; } = Task.CompletedTask;

    /// <summary>Asks for a refresh. Does nothing while one is already scheduled.</summary>
    internal void Notify()
    {
        if (_lifetime.IsCancellationRequested || Interlocked.Exchange(ref _scheduled, 1) == 1)
        {
            return;
        }

        Scheduled = RefreshAfterDelayAsync();
    }

    private async Task RefreshAfterDelayAsync()
    {
        try
        {
            await _delay(_lifetime).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Cleared before the refresh reads the feed, so a frame recorded while it renders schedules the next refresh
        // instead of folding into one that has already taken its snapshot.
        Volatile.Write(ref _scheduled, 0);
        if (!_lifetime.IsCancellationRequested)
        {
            _refresh();
        }
    }
}
