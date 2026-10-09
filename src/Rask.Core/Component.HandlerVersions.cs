namespace Rask.Core;

public abstract partial class Component
{
    /// <summary>
    ///     The number the page about to be sent carries, or null when it carries none: a page whose handlers are
    ///     where they were needs no new number, a <paramref name="whole" /> page always says which one it is.
    /// </summary>
    internal int? HandlerVersionToSend(bool whole)
    {
        var state = Live.HandlerState ??= new HandlerState();
        var version = state.Moved ? state.Version + 1 : state.Version;
        return whole || state.Moved ? version - state.Floor : null;
    }

    /// <summary>The page the last walk rendered was sent to the browser.</summary>
    internal void HandlersSent()
    {
        if (_live?.HandlerState is { Moved: true } state)
        {
            state.Version++;
            state.Moved = false;
        }
    }

    /// <summary>
    ///     The page the last walk rendered was delivered as a document. A browser that has only the document counts
    ///     from zero, so this page is zero from here on.
    /// </summary>
    internal void HandlersDelivered()
    {
        HandlersSent();
        if (_live?.HandlerState is { } state)
        {
            state.Floor = state.Version;
        }
    }
}
