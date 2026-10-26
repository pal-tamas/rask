namespace Rask.Core.Live;

/// <summary>
///     A lifecycle hook, or a read, keeping the event that started it alive: released once, when it is done — or,
///     when its component has left the page, a grace period after that, so a hook that never ends (a loop over a
///     stream) does not keep its event's services for the rest of the session.
/// </summary>
internal sealed class EventHold(EventServices services, EventLife life)
{
    private int _released;
    private CancellationTokenRegistration _teardown;
    private TimeSpan _grace;

    /// <summary>Releases the hold when <paramref name="work" /> completes, however it ends.</summary>
    internal void Until(Task work) =>
        // Nothing awaits it: the continuation is the release itself.
        _ = work.ContinueWith(
            static (_, state) => ((EventHold)state!).Release(),
            this,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    /// <summary>Releases the hold <paramref name="grace" /> after <paramref name="lifetime" /> ends, if it is still held.</summary>
    internal void OrAfter(TimeSpan grace, CancellationToken lifetime)
    {
        _grace = grace;

        // Nothing awaits the release: it ends the hold by itself, a grace period after the component has gone.
        _teardown = lifetime.Register(static state => _ = ((EventHold)state!).ReleaseAfterGraceAsync(), this);
    }

    private async Task ReleaseAfterGraceAsync()
    {
        await Task.Delay(_grace, CancellationToken.None).ConfigureAwait(false);
        Release();
    }

    internal void Release()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
        {
            return;
        }

        _teardown.Dispose();
        services.Release(life);
    }
}
