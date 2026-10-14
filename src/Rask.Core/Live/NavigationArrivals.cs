namespace Rask.Core.Live;

/// <summary>The navigations a session has been asked for and has not begun.</summary>
/// <remarks>
///     <para>
///         The reader who presses a second link before the first has begun has asked for another page, so the
///         first is skipped (<see cref="Starts" />) and <see cref="AnyQueued" /> tells whatever would render
///         next that its page is already on the way out.
///     </para>
///     <para>
///         Shared by both hosts. The server reads a navigation on its socket's thread while a dispatch runs on
///         another, so every member is safe to call from either; the browser has one thread and pays nothing for it.
///     </para>
/// </remarks>
internal sealed class NavigationArrivals
{
    private int _queued;

    /// <summary>Whether a navigation has been read and has not yet begun.</summary>
    internal bool AnyQueued => Volatile.Read(ref _queued) > 0;

    /// <summary>A navigation was read, and waits its turn.</summary>
    internal void Read() => Interlocked.Increment(ref _queued);

    /// <summary>
    ///     The navigation read earliest takes its turn. False when a newer one is already waiting behind it, and
    ///     this one would be rendered only to be replaced.
    /// </summary>
    internal bool Starts() => Interlocked.Decrement(ref _queued) == 0;
}
