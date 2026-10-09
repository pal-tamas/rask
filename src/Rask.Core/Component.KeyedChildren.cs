using Rask.Core.Diagnostics;
using Rask.Core.Live;

namespace Rask.Core;

public abstract partial class Component
{
    // GetOrCreateChild counts positions up from 0 and AdoptChild down from int.MaxValue, so an ordinal below
    // this is one an entry filed.
    private const int EntryPositionLimit = int.MaxValue / 2;

    /// <summary>
    ///     Settles which instance a keyed child actually is, replacing the one its entry handed us.
    /// </summary>
    /// <remarks>
    ///     Called by the <c>Key</c> chain step, on the PARENT, after the entry that built
    ///     <paramref name="provisional" />. `Key` has always been the diff codec's reconciliation identity
    ///     (<c>data-rask-key</c>); until #685 it was not the parent's, so a keyed row's own state — private
    ///     fields, a <c>OnMount</c> subscription — followed its POSITION instead of its item.
    ///     <para>
    ///         The key is looked up among everything this parent writes, of the child's type: the Key step
    ///         runs before the element the child will sit under has been given its children, and a child can
    ///         be built before that element exists at all, so "the same element" is not something a claim
    ///         can ask. A key written twice in one render is therefore told apart by the order it is
    ///         written in, each use keeping an instance of its own, and reported once (#1215).
    ///     </para>
    /// </remarks>
    internal T ClaimKeyedChild<T>(
        T provisional,
        object key,
        (Type Type, int Ordinal) slot,
        Func<IServiceProvider, Component> create) where T : Component
    {
        // The instance's OWN type, not T. `Row[body].Key(id)` reaches here through the generic Key over
        // Component — the indexer hands back Component — and filing that claim under typeof(Component) would
        // key it apart from every other Row, leave Row recycled by position, and miss the slot below.
        var type = provisional.GetType();

        // Whether the entry handed over an instance an unkeyed child has held since an earlier render: that
        // one is not this key's to keep, whatever else the key turns out to own.
        var held = GiveBackToUnkeyedSiblings(provisional, type, slot, out var siblings);

        var keyed = Live.KeyedChildren ??= new Dictionary<(Type, object), Component>();
        var repeated = keyed.ContainsKey((type, key));
        var chosen = provisional;
        if (PreviousKeyedChild(siblings, type, key, repeated) is T kept)
        {
            chosen = kept;
        }
        else if (held)
        {
            // A key that is new this render gets an instance that never ran (#685).
            chosen = (T)NewChild(create, LiveRenderContext.Current?.Services);
        }

        if (!ReferenceEquals(chosen, provisional))
        {
            Replace(provisional, chosen);

            // Set aside for the next entry of this type. A spare is handed out as new, so only an instance
            // that never ran may become one.
            if (!held && !provisional.HasInitializedInternal)
            {
                siblings.Spare = provisional;
            }
        }

        if (repeated)
        {
            siblings.FileRepeat(key, chosen);
        }
        else
        {
            keyed[(type, key)] = chosen;
        }

        // Re-file this frame's positional slot onto whichever instance the key settled on, so the
        // alive-set walk sees the child that is actually rendered here. The slot the ENTRY filed, not the
        // parent's last one: with Key free to come last (#1118), a step's argument can build another child
        // in between — `Row.Badge(Span["b"]).Key(id)` — and the last slot is then the Span's.
        if (Live.Children is not null && slot.Type == type)
        {
            Live.Children[slot] = chosen;
        }

        return chosen;
    }

    private bool GiveBackToUnkeyedSiblings(
        Component provisional,
        Type type,
        (Type Type, int Ordinal) slot,
        out KeyedSiblings siblings)
    {
        var keyedTypes = Live.KeyedTypes ??= new Dictionary<Type, KeyedSiblings>();
        if (keyedTypes.TryGetValue(type, out var known))
        {
            siblings = known;
            return siblings.GiveBack(provisional);
        }

        // From now on this parent identifies the type by key rather than by position — see LiveState.KeyedTypes.
        keyedTypes[type] = siblings = new KeyedSiblings();
        return TakeOverUnkeyedSiblings(siblings, provisional, slot);
    }

    // The instance this use of the key had last render. A key already written this render is a REPEAT, which
    // has no entry of its own in the keyed map — the first use owns that.
    private Component? PreviousKeyedChild(KeyedSiblings siblings, Type type, object key, bool repeated)
    {
        if (!repeated)
        {
            return Live.PreviousKeyedChildren?.GetValueOrDefault((type, key));
        }

        ReportRepeatedKey(siblings, type, key);
        return siblings.PreviousRepeat(key);
    }

    // The provisional instance's children, which is what GetOrCreateChild's "a childless render must not inherit
    // the last one's subtree" wants: null when the indexer comes after Key, as it usually does, and the subtree
    // it already wrote when Key comes after the indexer (#1118).
    private static void Replace(Component provisional, Component chosen)
    {
        chosen.Children = provisional.Children;
        chosen.RenderHandle ??= provisional.RenderHandle;
        provisional.Children = null;
    }

    // The first Key this parent has seen on the type. Until now its children were identified by their ordinal
    // among ALL the parent's children; the ones without a key go on being told apart by their order among
    // themselves, so they are handed over in that order — the ones already written this render, then whatever
    // last render left that nothing has taken yet. True when the provisional instance is one of the latter.
    private bool TakeOverUnkeyedSiblings(KeyedSiblings siblings, Component provisional, (Type Type, int Ordinal) slot)
    {
        if (Live.Children is { } written)
        {
            for (var position = 0; position < Live.ChildPositions; position++)
            {
                if (written.TryGetValue((slot.Type, position), out var sibling))
                {
                    siblings.Hold(sibling);
                }
            }

            // The provisional instance included, then given back: that leaves the children written after it —
            // inside one of its own steps — at the place in the order they will have on every later render.
            siblings.GiveBack(provisional);
        }

        if (Live.PreviousChildren is not { Count: > 0 } before)
        {
            return false;
        }

        var left = new List<(int Position, Component Instance)>();
#pragma warning disable S3267 // a filter over a dictionary, once in the parent's life: no closure for it
        foreach (var ((type, position), instance) in before)
#pragma warning restore S3267
        {
            if (type == slot.Type && position < EntryPositionLimit
                                  && (ReferenceEquals(instance, provisional) || !IsWrittenChild(instance)))
            {
                left.Add((position, instance));
            }
        }

        left.Sort(static (a, b) => a.Position.CompareTo(b.Position));
        foreach (var (_, instance) in left)
        {
            siblings.Offer(instance);
        }

        return before.TryGetValue(slot, out var recycled) && ReferenceEquals(recycled, provisional);
    }

    private bool IsWrittenChild(Component instance)
    {
#pragma warning disable S3267 // hot path: no enumerator/closure allocation
        foreach (var written in Live.Children!.Values)
#pragma warning restore S3267
        {
            if (ReferenceEquals(written, instance))
            {
                return true;
            }
        }

        return false;
    }

    // Warning, not Error: each use keeps an instance of its own and shows its own values, so nothing is wrong
    // on screen. What the author has to know is that the uses are told apart by ORDER — the state one holds
    // moves to the next when an earlier one stops being written.
    private void ReportRepeatedKey(KeyedSiblings siblings, Type type, object key)
    {
        if (siblings.RepeatReported)
        {
            return;
        }

        siblings.RepeatReported = true;
        RaskDiagnostics.ReportOnce(
            $"repeatedkey:{GetType().FullName}:{type.FullName}",
            RaskLogLevel.Warning,
            "Rask.Live",
            () => $"{GetType().Name} writes two {type.Name} components with Key \"{key}\" in one render. A " +
                  "component's key names ONE component of its type among everything the component that writes " +
                  "it renders, not only among the children of one element. The two are told apart by the order " +
                  "they are written in, so the state one holds moves to the other when the first is no longer " +
                  "written. Give each a Key of its own, for example by prefixing it with the list it is in.");
    }
}
