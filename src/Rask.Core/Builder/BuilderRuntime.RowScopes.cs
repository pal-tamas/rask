using Rask.Core.Components;
using Rask.Core.Live;

namespace Rask.Core;

// A COMPONENT WRITTEN INSIDE A KEYED ROW BELONGS TO THAT ROW (#1309).
//
// Without a key of its own a component is the n-th of its type its owner writes. Inside a list that was the n-th
// row's — until a row above it went, and every later row's component moved up one instance: a press made a moment
// earlier on Bothman's delete then ran on the instance that now draws Geidt's row, and deleted Geidt. So once a
// keyed node is complete — it has its key and its children, whichever came second — the unkeyed components it holds
// are claimed under the row's key and their order among the row's children of their type, through the same claim a
// written Key goes through (Component.ClaimScopedChild). Position then counts only inside one row: a handler bound
// in row A can never reach a component of row B.
//
// What it costs: one walk of the row's children when the row is complete, and for each unkeyed component in it a
// claim instead of a positional lookup. Nothing for a page without keys, a keyed node without children (a component
// row whose content is its own Render()), or a render with no live context. The walk stops at a nested keyed node,
// which scoped its own children when it was complete: a component is scoped by its NEAREST keyed ancestor.
public static partial class BuilderRuntime
{
    // Per thread for the reasons the slot stack is: reused across renders, so the steady state allocates nothing,
    // and appended to and truncated back like a stack.
    [ThreadStatic]
    private static List<RowChild>? _rowChildren;

    [ThreadStatic]
    private static List<(Type Type, int Count)>? _rowTypeCounts;

    /// <summary>
    ///     Claims the unkeyed components <paramref name="row" /> holds under its <paramref name="key" />, once it
    ///     has both. Called by the indexer when the key came first, and by the Key step when the children did.
    /// </summary>
    internal static void ScopeRow(Component row, object key)
    {
        var slots = _slots;
        if (slots is not { Count: > 0 })
        {
            return;
        }

        // The component whose Render() is building the row: what the row holds is claimed among ITS children. Its
        // slots are the top of the stack — its children render later, during the walk — so the search for one stops
        // where they end.
        var owner = LiveRenderContext.Walking?.WalkParent;
        if (owner is null)
        {
            return;
        }

        var found = _rowChildren ??= [];
        var counts = _rowTypeCounts ??= [];
        var start = found.Count;
        var countsStart = counts.Count;
        try
        {
            Collect(row, found, counts, countsStart);
            ClaimCollected(owner, key, slots, found, start);
        }
        finally
        {
            found.RemoveRange(start, found.Count - start);
            counts.RemoveRange(countsStart, counts.Count - countsStart);
        }
    }

    // Last written first. An entry of a type its parent claims by key hands out the instance the next unkeyed child
    // held, and a claim gives it back — which only keeps that order when what is given back is the last one handed
    // out (KeyedSiblings.GiveBack). Walk order is the order the chains ran in, so the reverse of it is.
    private static void ClaimCollected(Component owner, object key, List<EntrySlot> slots, List<RowChild> found, int start)
    {
        var top = slots.Count - 1;
        var cursor = top;
        for (var i = found.Count - 1; i >= start; i--)
        {
            var (child, container, index, ordinal) = found[i];
            // Usually straight below the last one found: the walk meets the row's children in the order their chains
            // ran. A row's cells may have been built before the row itself (`Tr.Key(k)[cells]`), which only moves
            // where the run starts.
            var at = FindSlot(slots, child, owner, cursor);
            if (at < 0 && cursor < top)
            {
                at = FindSlot(slots, child, owner, top);
            }

            // Not built by an entry of this owner — the app built it, or handed it in from elsewhere: whoever built
            // it owns its identity.
            if (at < 0)
            {
                continue;
            }

            cursor = at - 1;
            var slot = slots[at];
            var chosen = owner.ClaimScopedChild(child, key, ordinal, slot.ChildSlot, slot.Create);
            if (ReferenceEquals(chosen, child))
            {
                continue;
            }

            // As a Key step does (ClaimKey): the whole chain ran on the provisional instance, so all of it is replayed
            // onto the one the row kept, and the deferred reset and commit follow the slot onto it.
            slot.Copy?.Invoke(child, chosen, ~slot.PendingMask);
            (child as ICarriesHandSteps)?.CarryTo(chosen);
            slots[at] = slot with { Target = chosen };
            Place(container, index, chosen);
        }
    }

    private static int FindSlot(List<EntrySlot> slots, Component target, Component owner, int from)
    {
        for (var i = from; i >= 0 && ReferenceEquals(slots[i].Parent, owner); i--)
        {
            if (ReferenceEquals(slots[i].Target, target))
            {
                return i;
            }
        }

        return -1;
    }

    // The unkeyed components under `node`, in walk order, each with where it sits and its place among the row's
    // children of its type. Only a child held in an array or a list can be put back in its place by another instance,
    // and one in any other collection keeps the instance position gave it, and so does one held in a prop.
    private static void Collect(Component node, List<RowChild> found, List<(Type Type, int Count)> counts, int countsStart)
    {
        switch (node.Children)
        {
            case Component?[] array:
                for (var i = 0; i < array.Length; i++)
                {
                    Visit(array[i], node, i, found, counts, countsStart);
                }

                break;
            case List<Component?> list:
                for (var i = 0; i < list.Count; i++)
                {
                    Visit(list[i], node, i, found, counts, countsStart);
                }

                break;
            case { } other:
                foreach (var child in other)
                {
                    Visit(child, node, -1, found, counts, countsStart);
                }

                break;
        }
    }

    private static void Visit(
        Component? child, Component container, int index, List<RowChild> found, List<(Type Type, int Count)> counts, int countsStart)
    {
        // A keyed child is its own: a component by the key it was claimed under, a nested row by the scope it gave
        // its own children when it was complete.
        if (child is null || child.Key is not null)
        {
            return;
        }

        if (index >= 0 && child is not (Element or Text or Raw or Fragment or IKeyedInItsList))
        {
            found.Add(new RowChild(child, container, index, NextOrdinal(counts, countsStart, child.GetType())));
        }

        if (child.Children is not null)
        {
            Collect(child, found, counts, countsStart);
        }
    }

    private static int NextOrdinal(List<(Type Type, int Count)> counts, int start, Type type)
    {
        for (var i = start; i < counts.Count; i++)
        {
            if (counts[i].Type == type)
            {
                var (_, count) = counts[i];
                counts[i] = (type, count + 1);
                return count;
            }
        }

        counts.Add((type, 1));
        return 0;
    }

    private static void Place(Component container, int index, Component chosen)
    {
        switch (container.Children)
        {
            case Component?[] array:
                array[index] = chosen;
                break;
            case List<Component?> list:
                list[index] = chosen;
                break;
        }
    }

    // One unkeyed component a complete row holds: where it sits, and its place among the row's children of its type.
    private readonly record struct RowChild(Component Child, Component Container, int Index, int Ordinal);
}
