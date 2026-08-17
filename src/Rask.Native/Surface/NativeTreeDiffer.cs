namespace Rask.Native.Surface;

/// <summary>
///     Turns last frame's <see cref="NativeNode" /> tree and this frame's into the minimal ordered
///     <see cref="NativePatch" /> list that carries one into the other — the native counterpart of the HTML
///     diff codec, and the reason a keystroke in a text field repaints one label instead of rebuilding a
///     screenful of <c>UIView</c>s.
/// </summary>
/// <remarks>
///     Children reconcile by key when the parent uses keys and by position when it does not, which is what lets
///     a reordered list MOVE its row views (keeping their scroll position, focus and animation state) instead
///     of rewriting every row's contents in place.
/// </remarks>
internal static class NativeTreeDiffer
{
    /// <summary>
    ///     Diffs <paramref name="oldRoot" /> into <paramref name="newRoot" />.
    /// </summary>
    /// <returns>
    ///     The ordered patches, or <c>null</c> when the two roots cannot be reconciled (their kinds differ) and
    ///     the caller must re-mount the tree wholesale instead.
    /// </returns>
    public static List<NativePatch>? Diff(NativeNode oldRoot, NativeNode newRoot)
    {
        if (oldRoot.Kind != newRoot.Kind)
        {
            // A patch is addressed relative to a root that survives it; a root whose kind changed does not.
            return null;
        }

        var patches = new List<NativePatch>();
        var path = new List<int>();
        DiffNode(oldRoot, newRoot, path, patches);
        return patches;
    }

    // Pre-order: this node's own props first, then its children, so a backend applying the list in order never
    // sees a child op addressed through a node it has not updated yet.
    private static void DiffNode(NativeNode oldNode, NativeNode newNode, List<int> path, List<NativePatch> patches)
    {
        var changed = DiffProps(oldNode.Props, newNode.Props);
        if (changed is not null)
        {
            patches.Add(new NativePatch
            {
                Kind = NativePatchKind.SetProps,
                Path = [.. path],
                Props = changed,
            });
        }

        DiffChildren(oldNode.Children, newNode.Children, path, patches);
    }

    /// <summary>
    ///     Merge-walks two id-sorted prop lists. Returns only the props that actually changed — including the
    ///     ones that went away, carried as <see cref="NativePropValue.Unset" /> — or <c>null</c> when the two
    ///     lists agree, which is the common case and must not allocate.
    /// </summary>
    private static NativeProp[]? DiffProps(NativeProp[] oldProps, NativeProp[] newProps)
    {
        List<NativeProp>? changed = null;
        int i = 0, j = 0;
        while (i < oldProps.Length || j < newProps.Length)
        {
            if (i < oldProps.Length && j < newProps.Length && oldProps[i].Id == newProps[j].Id)
            {
                if (oldProps[i].Value != newProps[j].Value)
                {
                    (changed ??= []).Add(newProps[j]);
                }

                i++;
                j++;
            }
            else if (j >= newProps.Length || (i < oldProps.Length && oldProps[i].Id < newProps[j].Id))
            {
                // Present last frame, absent now — tell the backend to put the property back to its default.
                (changed ??= []).Add(new NativeProp(oldProps[i].Id, NativePropValue.Unset));
                i++;
            }
            else
            {
                (changed ??= []).Add(newProps[j]);
                j++;
            }
        }

        return changed?.ToArray();
    }

    private static void DiffChildren(
        NativeNode[] oldKids, NativeNode[] newKids, List<int> path, List<NativePatch> patches)
    {
        if (oldKids.Length == 0 && newKids.Length == 0)
        {
            return;
        }

        if (!UsesKeys(oldKids) && !UsesKeys(newKids))
        {
            DiffChildrenByPosition(oldKids, newKids, path, patches);
            return;
        }

        DiffChildrenByKey(oldKids, newKids, path, patches);
    }

    private static bool UsesKeys(NativeNode[] kids)
    {
        foreach (var kid in kids)
        {
            if (kid.Key is not null)
            {
                return true;
            }
        }

        return false;
    }

    // No keys anywhere: identity is position. Recurse over the common prefix, then trim or extend the tail.
    private static void DiffChildrenByPosition(
        NativeNode[] oldKids, NativeNode[] newKids, List<int> path, List<NativePatch> patches)
    {
        var common = Math.Min(oldKids.Length, newKids.Length);
        for (var i = 0; i < common; i++)
        {
            DiffChild(oldKids[i], newKids[i], i, path, patches);
        }

        // Remove from the tail down, so each index is still valid when its op runs.
        for (var i = oldKids.Length - 1; i >= common; i--)
        {
            patches.Add(new NativePatch { Kind = NativePatchKind.Remove, Path = [.. path], Index = i });
        }

        for (var i = common; i < newKids.Length; i++)
        {
            patches.Add(new NativePatch
            {
                Kind = NativePatchKind.Insert,
                Path = [.. path],
                Index = i,
                Node = newKids[i],
            });
        }
    }

    // Keyed reconciliation. Every op is expressed against the child list AS IT STANDS when that op runs, which
    // is why `cur` is mutated alongside the patches being emitted: a backend replaying them with plain
    // remove-at/insert-at ends up with exactly this list.
    private static void DiffChildrenByKey(
        NativeNode[] oldKids, NativeNode[] newKids, List<int> path, List<NativePatch> patches)
    {
        var wanted = new HashSet<ChildKey>();
        for (var j = 0; j < newKids.Length; j++)
        {
            wanted.Add(new ChildKey(newKids[j].Key, j));
        }

        var cur = new List<(ChildKey Key, NativeNode Node)>(oldKids.Length);
        for (var i = 0; i < oldKids.Length; i++)
        {
            cur.Add((new ChildKey(oldKids[i].Key, i), oldKids[i]));
        }

        // Drop everything the new frame no longer wants, tail-first so earlier indices stay valid.
        for (var i = cur.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(cur[i].Key))
            {
                patches.Add(new NativePatch { Kind = NativePatchKind.Remove, Path = [.. path], Index = i });
                cur.RemoveAt(i);
            }
        }

        for (var j = 0; j < newKids.Length; j++)
        {
            var want = new ChildKey(newKids[j].Key, j);
            if (j < cur.Count && cur[j].Key.Equals(want))
            {
                DiffChild(cur[j].Node, newKids[j], j, path, patches);
                continue;
            }

            var from = IndexOfKey(cur, want, j);
            if (from >= 0)
            {
                patches.Add(new NativePatch
                {
                    Kind = NativePatchKind.Move,
                    Path = [.. path],
                    FromIndex = from,
                    Index = j,
                });
                var moved = cur[from];
                cur.RemoveAt(from);
                cur.Insert(j, moved);
                DiffChild(moved.Node, newKids[j], j, path, patches);
                continue;
            }

            patches.Add(new NativePatch
            {
                Kind = NativePatchKind.Insert,
                Path = [.. path],
                Index = j,
                Node = newKids[j],
            });
            cur.Insert(j, (want, newKids[j]));
        }
    }

    private static int IndexOfKey(List<(ChildKey Key, NativeNode Node)> cur, ChildKey want, int from)
    {
        for (var i = from; i < cur.Count; i++)
        {
            if (cur[i].Key.Equals(want))
            {
                return i;
            }
        }

        return -1;
    }

    // A matched pair either patches in place or, when the kind changed, gets replaced outright — a UILabel
    // cannot become a UIButton.
    private static void DiffChild(
        NativeNode oldKid, NativeNode newKid, int index, List<int> path, List<NativePatch> patches)
    {
        path.Add(index);
        try
        {
            if (oldKid.Kind != newKid.Kind)
            {
                patches.Add(new NativePatch
                {
                    Kind = NativePatchKind.Replace,
                    Path = [.. path],
                    Node = newKid,
                });
                return;
            }

            DiffNode(oldKid, newKid, path, patches);
        }
        finally
        {
            path.RemoveAt(path.Count - 1);
        }
    }

    /// <summary>
    ///     A child's reconciliation identity: its <c>Key</c> when it has one, else its position. Keyed and
    ///     unkeyed children never match each other, so a list that gains keys re-mounts its rows once rather
    ///     than matching them against positional identities that mean something different.
    /// </summary>
    private readonly struct ChildKey(string? key, int index) : IEquatable<ChildKey>
    {
        private readonly string? _key = key;
        private readonly int _index = index;

        public bool Equals(ChildKey other) =>
            _key is null || other._key is null
                ? _key is null && other._key is null && _index == other._index
                : string.Equals(_key, other._key, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is ChildKey other && Equals(other);

        public override int GetHashCode() =>
            _key is null ? _index : StringComparer.Ordinal.GetHashCode(_key);
    }
}
