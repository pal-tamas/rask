using System.Runtime.CompilerServices;
using Rask.Core;
using Rask.Core.Live;

namespace Rask.Query;

/// <summary>
///     The queries and commands one component asked for inside its <c>Render</c>, kept from one render to
///     the next so the same call gets the same handle back.
/// </summary>
/// <remarks>
///     <para>
///         A slot is found by where it was called — the call site — and which call it was at that site in
///         this render: the loop's third iteration is slot three of that line, every render. A caller that
///         wants identity to follow an item instead of its position passes a key, and the slot is found by
///         the key. No rule about calling in a fixed order: a call site skipped by an <c>if</c> numbers
///         only its own calls, so it cannot shift anyone else's.
///     </para>
///     <para>
///         A slot neither asked for nor read in a render is released as that render returns: its query
///         stops watching its entry, so the entry starts the GC clock like any other nothing watches. That
///         includes a render that asks for nothing at all — the release hangs off the component's render,
///         not off the next call. The query is suspended, not disposed, and wakes on its next read: a
///         constructor or <c>field ??=</c> call also lands in a slot, and the component keeping that query
///         must never find it dead. Held weakly by the component, so a component that leaves the tree takes
///         its slots with it.
///     </para>
/// </remarks>
internal sealed class RenderSlots
{
    private static readonly ConditionalWeakTable<Component, RenderSlots> Table = [];

    private readonly Dictionary<SlotId, Slot> _slots = [];
    private readonly Dictionary<CallSite, int> _counters = [];
    private long _generation;

    /// <summary>
    ///     The slot for this call in the component rendering now, or null outside a render — where the
    ///     caller makes a handle of its own, exactly as the injected client does.
    /// </summary>
    public static Slot? For(string file, int line, object? key)
    {
        if (LiveRenderContext.RenderOwner(out var generation) is not { } component || generation == 0)
        {
            return null;
        }

        return Table.GetValue(component, Create).Take(generation, new CallSite(file, line), key);
    }

    private static RenderSlots Create(Component component)
    {
        var slots = new RenderSlots();
        component.AddAfterRenderInternal(slots.EndRender);
        return slots;
    }

    private Slot Take(long generation, CallSite site, object? key)
    {
        if (generation != _generation)
        {
            _generation = generation;
            _counters.Clear();
        }

        SlotId id;
        if (key is null)
        {
            _counters.TryGetValue(site, out var occurrence);
            _counters[site] = occurrence + 1;
            id = new SlotId(site, null, occurrence);
        }
        else
        {
            id = new SlotId(site, key, 0);
        }

        if (!_slots.TryGetValue(id, out var slot))
        {
            slot = new Slot();
            _slots[id] = slot;
        }

        slot.LastUsed = generation;
        return slot;
    }

    /// <summary>Drops every slot the render that just returned did not ask for.</summary>
    private void EndRender()
    {
        if (_slots.Count == 0)
        {
            return;
        }

        LiveRenderContext.RenderOwner(out var current);
        List<SlotId>? stale = null;
        foreach (var (id, slot) in _slots)
        {
            if (slot.LastUsed != current)
            {
                (stale ??= []).Add(id);
            }
        }

        if (stale is null)
        {
            return;
        }

        foreach (var id in stale)
        {
            (_slots[id].Handle as IRenderSlotHandle)?.ReleaseUnlessReadIn(current);
            _slots.Remove(id);
        }
    }

    private readonly record struct CallSite(string File, int Line);

    private readonly record struct SlotId(CallSite Site, object? Key, int Occurrence);

    /// <summary>One call's handle, and what it was last asked for so an unchanged call costs nothing.</summary>
    internal sealed class Slot
    {
        public object? Handle { get; set; }

        /// <summary>What the handle points at now — a message, an input, a key — compared on the next call.</summary>
        public object? Last { get; set; }

        public long LastUsed { get; set; }
    }
}

/// <summary>A handle a render slot can set aside when a render stops using it.</summary>
internal interface IRenderSlotHandle
{
    /// <summary>Stops watching, unless it was read during <paramref name="generation" />.</summary>
    void ReleaseUnlessReadIn(long generation);
}
