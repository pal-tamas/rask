using System.Runtime.CompilerServices;

namespace Rask.TestSupport;

/// <summary>
///     Parks the repaint of a page whose handler is still working, and has that handler navigate under it.
/// </summary>
/// <remarks>
///     The handler takes one with <see cref="Park" />, awaits <see cref="Walking" /> and then navigates; the app
///     calls <see cref="Reached(object)" /> from its root's <c>Render</c>, above the router. A session that lets
///     the two run side by side has the route moved by the time the walk reaches the router. One that makes them
///     take turns keeps the handler out until the walk is over — so the wait here runs out, which is what the
///     test is about, and costs it that long.
/// </remarks>
public sealed class RepaintHold
{
    private static readonly TimeSpan _heldFor = TimeSpan.FromMilliseconds(250);

    // By session, so a test's hold parks no walk but its own: the key is something only that session has.
    private static readonly ConditionalWeakTable<object, RepaintHold> _parked = new();

    private readonly TaskCompletionSource _walking = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ManualResetEventSlim _navigated = new();

    /// <summary>Completes when a walk of the page has reached <see cref="Reached" />.</summary>
    public Task Walking => _walking.Task;

    /// <summary>Parks the next walk of the session that owns <paramref name="session" />.</summary>
    public static RepaintHold Park(object session)
    {
        var hold = new RepaintHold();
        _parked.AddOrUpdate(session, hold);
        return hold;
    }

    /// <summary>A walk of that session is here: the first one parked waits for the handler to navigate.</summary>
    public static void Reached(object session)
    {
        if (_parked.TryGetValue(session, out var hold) && hold._walking.TrySetResult())
        {
            hold._navigated.Wait(_heldFor, CancellationToken.None);
        }
    }

    /// <summary>The handler has navigated.</summary>
    public void Navigated() => _navigated.Set();
}
