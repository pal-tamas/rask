namespace Rask.Core;

// A PRESS MADE AGAIN FROM A PAGE THE BROWSER WAS TOLD IT HOLDS reaches the handler it was made on, however many pages
// the host has drawn since.
//
// A `.Confirm` yes presses its button again once the browser holds every page sent before the answer to its
// `caughtUp` question (rask-confirm.ts), and is read from exactly that page. A page drawn anew every few dozen ms —
// a board while a long sending reports its progress — is many pages further on by the time the press arrives: past
// the two an event outlives a render by (Component.StaleEvents.cs), and its handler a new closure in every one. So
// the answer keeps the handlers of the page it follows, and an event read from that page finds in it the handler the
// reader pressed. What runs is still a handler the page has now that is the SAME as it — in its slot, moved, or
// told apart from its siblings — so the kept page reaches nothing a click could not. One page is kept, until the
// browser sends something read from a later one.
public abstract partial class Component
{
    /// <summary>
    ///     Keeps the handlers of the page the browser is about to be told it holds: the last page sent. Called under
    ///     the render lock, as that answer is sent, so no walk moves them in between.
    /// </summary>
    internal void KeepPageForPress()
    {
        if (_live is not { HandlerState: { Unsent: false } state, Handlers: { } map })
        {
            return;
        }

        state.Kept = new Dictionary<string, (Component Owner, Delegate Handler)>(map, StringComparer.Ordinal);
        state.KeptVersion = state.Version;
    }

    // A browser that sent something read from a page after the kept one reads nothing from it any more: its events
    // leave in the order they were read.
    private static void ForgetKeptPageBefore(HandlerState root, long seen)
    {
        if (root.Kept is not null && seen > root.KeptVersion)
        {
            root.Kept = null;
        }
    }

    // The handler an event read from page `seen` was sent to, found through the kept page when it is that page.
    // Null when the kept page has nothing to say: another page, or none kept.
    private static bool? TryFindFromKeptPage(
        string id, long seen, HandlerState root, HandlerState state, int at,
        Dictionary<string, (Component Owner, Delegate Handler)> map, out (Component Owner, Delegate Handler) entry)
    {
        entry = default;
        if (root.Kept is not { } kept || seen != root.KeptVersion || !kept.TryGetValue(id, out var pressed))
        {
            return null;
        }

        var sought = pressed.Handler;
        return (map.TryGetValue(id, out entry) && HandlerSameness.Same(sought, entry.Handler))
               || TryFindMoved(state, map, sought, at, out entry)
               || TryFindAmongSiblings(state, map, sought, kept, out entry);
    }
}
