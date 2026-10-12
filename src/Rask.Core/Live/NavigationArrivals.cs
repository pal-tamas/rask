namespace Rask.Core.Live;

/// <summary>
///     The navigations a session has been asked for and has not begun, and the wait the newest of them ends.
/// </summary>
/// <remarks>
///     <para>
///         A frame that carries a navigation waits for the page it goes to (<see cref="QuiescentRender" />). The
///         reader who presses a second link meanwhile has asked for another page, so the first wait ends there
///         and its page is never sent: <see cref="Read" /> cancels <see cref="Superseded" />, and
///         <see cref="AnyQueued" /> tells whatever would render next that its page is already on the way out.
///     </para>
///     <para>
///         Shared by both hosts. The server reads a navigation on its socket's thread while a dispatch waits on
///         another, so every member is safe to call from either; the browser has one thread and pays nothing for it.
///     </para>
/// </remarks>
internal sealed class NavigationArrivals : IDisposable
{
    private int _queued;
    private CancellationTokenSource _wait = new();

    /// <summary>Whether a navigation has been read and has not yet begun.</summary>
    internal bool AnyQueued => Volatile.Read(ref _queued) > 0;

    /// <summary>Cancelled when the next navigation is read. Take it before asking <see cref="AnyQueued" />.</summary>
    internal CancellationToken Superseded
    {
        get
        {
            try
            {
                return Volatile.Read(ref _wait).Token;
            }
            catch (ObjectDisposedException)
            {
                // The session has gone: nothing is waited for any more.
                return new CancellationToken(canceled: true);
            }
        }
    }

    /// <summary>A navigation was read: whatever is waiting for an earlier one's page stops waiting.</summary>
    internal void Read()
    {
        Interlocked.Increment(ref _queued);
        var superseded = Interlocked.Exchange(ref _wait, new CancellationTokenSource());

        // Off the caller's thread: it goes back to reading, and the waiting dispatch unwinds on its own.
        _ = superseded.CancelAsync();
    }

    /// <summary>
    ///     The navigation read earliest takes its turn. False when a newer one is already waiting behind it, and
    ///     this one would be rendered only to be replaced.
    /// </summary>
    internal bool Starts() => Interlocked.Decrement(ref _queued) == 0;

    public void Dispose()
    {
        var wait = Volatile.Read(ref _wait);
        wait.Cancel();
        wait.Dispose();
    }
}
