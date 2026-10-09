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
//   * from the page before: the slot's previous handler is. The handler that runs is the one in the page as it is
//     now that is the SAME as it — the same method over equal captured values (HandlerSameness) — in that slot or,
//     when the slot was taken by another, in another slot of the same component.
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

        slot.Before = held;
        slot.BeforeSince = slot.Since;
        slot.Since = next;
    }

    /// <summary>Whether <paramref name="payload" /> was read from the page as it is now, or names no page at all.</summary>
    internal bool ReadFromThePageAsItIs(JsonElement payload) =>
        EventVersion.TryRead(payload, out var sent)
        && (sent is not { } version || _live?.HandlerState is not { } root || (long)version + root.Floor >= root.Version);

    // The handler an event is for: the one its id names in the page it was read from, as the page has it now.
    private bool TryFindHandler(string id, JsonElement payload, out (Component Owner, Delegate Handler) entry)
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
            return Missed(null, 0, version, root);
        }

        ref var slot = ref SlotAt(state, at.Slot);
        var seen = (long)version + root.Floor;
        if (seen >= slot.Since)
        {
            return (map.TryGetValue(id, out entry) && ReferenceEquals(entry.Handler, slot.Handler))
                   || (slot.Handler is { } vacated && TryFindMoved(state, map, vacated, at.Slot, out entry))
                   || Missed(at.Component, at.Slot, version, root);
        }

        root.EagerThrough = root.Version + EagerWalks;
        if (slot.Before is not { } before || seen < slot.BeforeSince)
        {
            return Missed(at.Component, at.Slot, version, root);
        }

        if (map.TryGetValue(id, out entry) && HandlerSameness.Same(before, entry.Handler))
        {
            // Proven once: the slot has held the same handler since its predecessor took it.
            slot.Since = slot.BeforeSince;
            slot.Before = null;
            return true;
        }

        return TryFindMoved(state, map, before, at.Slot, out entry) || Missed(at.Component, at.Slot, version, root);
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
                    $"Rask live: an event for {handler} ran nothing. It was read from page {sent}, the page is now {now}, and that handler is no longer there or cannot be shown to be the same one — see https://rask.sh/docs/js-interop-runtime#an-event-that-outlived-its-render"));
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
