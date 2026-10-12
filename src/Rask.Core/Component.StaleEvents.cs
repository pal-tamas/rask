using System.Globalization;
using System.Text.Json;
using Rask.Core.Diagnostics;
using Rask.Core.Live;

namespace Rask.Core;

// AN EVENT THAT OUTLIVED ITS RENDER reaches the handler it was sent to, or nothing.
//
// A handler id is a slot in its component's walk order, so the handler behind an id can change: a button that
// leaves hands its slot to the next one. An event says which page it was read from (`v`, counted by the pages the
// browser was sent whose handlers had moved), and each slot remembers since which page it has held what it holds.
//
//   * from that page or a later one: the slot's handler is the one the reader saw.
//   * from one of the two pages before: one of the two handlers the slot remembers is. The handler that runs is
//     the one in the page as it is now that is the SAME as it (HandlerSameness): in that slot, or in another slot
//     of the same component when the slot was taken by another, or — among the handlers one lambda made — the
//     one its siblings are told apart from it by.
//   * older than that, or nothing the same is left: nothing runs.
//
// What runs is always a handler the page has now, so `v` reaches nothing a click could not. An event that names no
// page is read against the page as it is — a frame written by hand, a browser holding a client from before this.
public abstract partial class Component
{
    // How many walks compare closures as they register them after an event from an older page arrived.
    private const int EagerWalks = 4;

    // The most slots of one component searched for a handler that moved.
    private const int MaxSlotsSearched = 4096;

    // Puts the handler a walk reached into its slot, and notes when it is a different one from the last.
    private static void Settle(ref HandlerSlot slot, Delegate handler, HandlerState root)
    {
        var held = slot.Handler;
        if (ReferenceEquals(held, handler))
        {
            return;
        }

        slot.Handler = handler;
        if (held is null || handler.Equals(held))
        {
            return;
        }

        // Comparing two closures reads their method and their captured values, which a walk cannot afford for
        // every handler. It is done here only while events from an older page are arriving; otherwise the slot is
        // marked as moved and the comparison waits for an event that needs it.
        var next = root.Version + 1;
        if (next <= root.EagerThrough && HandlerSameness.Same(held, handler))
        {
            return;
        }

        root.Moved = true;
        if (slot.Since == next)
        {
            return;
        }

        slot.Older = slot.Before;
        slot.OlderSince = slot.BeforeSince;
        slot.Before = held;
        slot.BeforeSince = slot.Since;
        slot.Since = next;
    }

    // `asking` is the batch's question — would this event's handler outlive a render? — which is not a dispatch:
    // an event that finds nothing is counted when it is dispatched, once.
    private bool TryFindHandler(
        string id, JsonElement payload, out (Component Owner, Delegate Handler) entry, bool asking = false)
    {
        entry = default;
        if (Live.Handlers is not { } map || !EventVersion.TryRead(payload, out var sent))
        {
            return false;
        }

        if (sent is not { } version)
        {
            return map.TryGetValue(id, out entry);
        }

        var root = Live.HandlerState;
        if (root?.Slots is not { } slots
            || !slots.TryGetValue(id, out var at)
            || at.Component._live?.HandlerState is not { } state
            || !ReferenceEquals(state.MintedUnder, root)
            || !string.Equals(IssuedSlotId(state, at.Slot), id, StringComparison.Ordinal))
        {
            return !asking && Missed(null, 0, version, root);
        }

        ref var slot = ref SlotAt(state, at.Slot);
        var seen = (long)version + root.Floor;
        if (seen < root.Version)
        {
            // More from that page are likely on their way: the next walks keep every slot's history connected.
            root.EagerThrough = root.Version + EagerWalks;
        }

        // The browser has the last page it was sent, and nothing built since is on its way: a walk since then
        // rendered that same page, so the handler under this id now is the one the reader pressed.
        var current = seen >= slot.Since || (seen >= root.Version && !root.Unsent);
        if (current && map.TryGetValue(id, out entry))
        {
            return true;
        }

        return ((current ? slot.Handler : HeldAt(ref slot, seen)) is { } sought
                && ((!current && TryFindInPlace(ref slot, map, id, sought, out entry))
                    || TryFindMoved(state, map, sought, at.Slot, out entry)
                    || TryFindAmongSiblings(state, map, sought, seen, out entry)))
               || (!asking && Missed(at.Component, at.Slot, version, root));
    }

    // No handler of the page captured exactly what this one did: a lambda also closes over what its render
    // worked out — a view of the list, a count — and that is new in every render. It is still the same handler
    // when it is the one its siblings, the handlers the same lambda made in the same walk, are told apart from
    // it by: the row, not the view every row shares (HandlerSameness.Pick).
    private static bool TryFindAmongSiblings(
        HandlerState state, Dictionary<string, (Component Owner, Delegate Handler)> map, Delegate sought, long seen,
        out (Component Owner, Delegate Handler) entry)
    {
        entry = default;
        var then = new List<Delegate>();
        var now = new List<(Component Owner, Delegate Handler)>();
        var issued = Math.Min(1 + (state.Rest?.Length ?? 0), MaxSlotsSearched);
        for (var i = 0; i < issued; i++)
        {
            ref var slot = ref i == 0 ? ref state.Slot0 : ref state.Rest![i - 1];
            if ((seen >= slot.Since ? slot.Handler : HeldAt(ref slot, seen)) is { } held
                && HandlerSameness.FromOneLambda(sought, held))
            {
                then.Add(held);
            }

            if (i < state.LocalCount
                && slot.Id is { } id
                && map.TryGetValue(id, out var here)
                && HandlerSameness.FromOneLambda(sought, here.Handler))
            {
                now.Add(here);
            }
        }

        var picked = HandlerSameness.Pick(sought, then, now.ConvertAll(static found => found.Handler));
        if (picked < 0)
        {
            return false;
        }

        entry = now[picked];
        return true;
    }

    // What the slot held in the page numbered `seen`, when that is one of the two handlers it remembers.
    private static Delegate? HeldAt(ref HandlerSlot slot, long seen)
    {
        if (slot.Before is { } before && seen >= slot.BeforeSince)
        {
            return before;
        }

        return slot.Older is { } older && seen >= slot.OlderSince ? older : null;
    }

    // The slot still holds the same handler: proven once, and remembered as held since the older one took it.
    private static bool TryFindInPlace(
        ref HandlerSlot slot, Dictionary<string, (Component Owner, Delegate Handler)> map, string id, Delegate sought,
        out (Component Owner, Delegate Handler) entry)
    {
        if (!map.TryGetValue(id, out entry) || !HandlerSameness.Same(sought, entry.Handler))
        {
            return false;
        }

        if (ReferenceEquals(sought, slot.Before))
        {
            slot.Since = slot.BeforeSince;
            slot.Before = slot.Older;
            slot.BeforeSince = slot.OlderSince;
        }
        else
        {
            slot.Since = slot.OlderSince;
            slot.Before = null;
        }

        slot.Older = null;
        return true;
    }

    // A handler that left its slot because one before it left or arrived is in another slot of the same component.
    private static bool TryFindMoved(
        HandlerState state, Dictionary<string, (Component Owner, Delegate Handler)> map, Delegate sought, int from,
        out (Component Owner, Delegate Handler) entry)
    {
        var count = Math.Min(state.LocalCount, MaxSlotsSearched);
        for (var i = 0; i < count; i++)
        {
            if (i != from
                && IssuedSlotId(state, i) is { } id
                && map.TryGetValue(id, out entry)
                && HandlerSameness.Same(sought, entry.Handler))
            {
                return true;
            }
        }

        entry = default;
        return false;
    }

    // Counted always, and said in Development: a click that ran nothing is otherwise invisible.
    private static bool Missed(Component? component, int slot, int sent, HandlerState? root)
    {
        StaleEvents.CountMissed();
        if (LiveOptions.IsDevelopment == true)
        {
            var handler = component is null
                ? "a handler whose component has left the page"
                : string.Create(CultureInfo.InvariantCulture, $"handler {slot} of {component.GetType().Name}");
            var now = root is null ? 0 : root.Version - root.Floor;
            RaskDiagnostics.Report(
                RaskLogLevel.Information, "Rask.Live",
                string.Create(CultureInfo.InvariantCulture,
                    $"Rask live: an event for {handler} ran nothing. It was read from page {sent}, the page is now {now}, and that handler is no longer there or cannot be shown to be the same one — see https://rask.sh/docs/guides/js-interop-runtime#an-event-that-outlived-its-render"));
        }

        return false;
    }

    // An unmounted component's ids name nothing any more, and the root must not keep it alive through them.
    private void ForgetHandlerSlots()
    {
        if (_live?.HandlerState is not { MintedUnder.Slots: { } slots, Forgotten: false } state)
        {
            return;
        }

        state.Forgotten = true;
        for (var i = 0; IssuedSlotId(state, i) is { } id; i++)
        {
            slots.Remove(id);
        }
    }

    // An instance the app kept and shows again renders under the ids it had.
    private static void RememberHandlerSlots(Component component, HandlerState state, HandlerState root)
    {
        state.Forgotten = false;
        var slots = root.Slots ??= new Dictionary<string, (Component, int)>(StringComparer.Ordinal);
        for (var i = 0; IssuedSlotId(state, i) is { } id; i++)
        {
            slots[id] = (component, i);
        }
    }
}
