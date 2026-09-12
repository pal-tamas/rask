namespace Rask.Ui;

/// <summary>
///     One visible node of a <see cref="UiTree{T,TKey}" />, with everything its row and the keyboard need.
/// </summary>
/// <param name="Node">The app's node.</param>
/// <param name="Key">Its key.</param>
/// <param name="Level">1 for a root, one more per generation — what <c>aria-level</c> says.</param>
/// <param name="Parent">The parent's index in the same list, or <c>-1</c> for a root.</param>
/// <param name="HasChildren">Whether it can be expanded at all.</param>
/// <param name="IsExpanded">Whether its children are in the list below it.</param>
/// <param name="SetSize">How many siblings it has, itself included.</param>
/// <param name="PosInSet">Its 1-based place among them.</param>
internal readonly record struct UiTreeRow<T, TKey>(
    T Node,
    TKey Key,
    int Level,
    int Parent,
    bool HasChildren,
    bool IsExpanded,
    int SetSize,
    int PosInSet);

/// <summary>
///     The arithmetic behind <see cref="UiTree{T,TKey}" />: which nodes are visible, in which order, and how they relate.
/// </summary>
/// <remarks>
///     Separate from the component for <c>UiSelectNav</c>'s reason — it is pure, and this is where a tree control actually
///     goes wrong. The visible nodes are a FLAT list in render order, so an arrow key moves to the next node on screen
///     rather than to the next node in some parallel structure: the cursor follows the eye, whether the tree renders
///     nested lists or a virtualized window.
/// </remarks>
internal static class UiTreeNav
{
    /// <summary>
    ///     The visible nodes, depth first, with the levels, parents and set positions each row needs.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Children are asked for only where they are needed: a node's selector runs when the row is built, and its
    ///         grandchildren stay unasked while it is collapsed. That is what lets a tree over a large or lazily-built
    ///         structure cost what is on screen.
    ///     </para>
    ///     <para>
    ///         A key seen twice is rendered once — the second sighting is dropped, and it is not counted among its
    ///         siblings, so <c>aria-setsize</c> stays true. That also ends a cycle: a node that is its own descendant has
    ///         been seen by the time the walk returns to it.
    ///     </para>
    /// </remarks>
    /// <param name="roots">The top-level nodes.</param>
    /// <param name="key">A node's identity.</param>
    /// <param name="children">A node's children, or null when the tree has no descent.</param>
    /// <param name="expanded">The keys whose children are shown.</param>
    /// <param name="index">Filled with each visible key's row index — the caller's map from key to row.</param>
    internal static List<UiTreeRow<T, TKey>> Flatten<T, TKey>(
        IEnumerable<T>? roots,
        Func<T, TKey> key,
        Func<T, IEnumerable<T>?>? children,
        HashSet<TKey> expanded,
        Dictionary<TKey, int> index)
        where TKey : notnull
    {
        var rows = new List<UiTreeRow<T, TKey>>();
        Walk(AsList(roots), 1, -1);
        return rows;

        void Walk(IReadOnlyList<T> siblings, int level, int parent)
        {
            // Deduplicated first, so the set size below counts the rows that will actually be there.
            var kept = new List<(T Node, TKey Key)>(siblings.Count);
            foreach (var node in siblings)
            {
                var k = key(node);
                if (index.TryAdd(k, -1))
                {
                    kept.Add((node, k));
                }
            }

            for (var i = 0; i < kept.Count; i++)
            {
                var (node, k) = kept[i];
                var kids = children is null ? null : AsList(children(node));
                var hasChildren = kids is { Count: > 0 };
                var isExpanded = hasChildren && expanded.Contains(k);

                index[k] = rows.Count;
                rows.Add(new UiTreeRow<T, TKey>(node, k, level, parent, hasChildren, isExpanded, kept.Count, i + 1));

                if (isExpanded)
                {
                    Walk(kids!, level + 1, rows.Count - 1);
                }
            }
        }
    }

    /// <summary>
    ///     Where <paramref name="at" />'s subtree ends: the next row at its level or above, or the end of the list.
    /// </summary>
    internal static int SubtreeEnd<T, TKey>(IReadOnlyList<UiTreeRow<T, TKey>> rows, int at)
    {
        var level = rows[at].Level;
        for (var i = at + 1; i < rows.Count; i++)
        {
            if (rows[i].Level <= level)
            {
                return i;
            }
        }

        return rows.Count;
    }

    /// <summary>The rows sharing <paramref name="at" />'s parent, itself included.</summary>
    internal static IEnumerable<UiTreeRow<T, TKey>> Siblings<T, TKey>(IReadOnlyList<UiTreeRow<T, TKey>> rows, int at)
    {
        var parent = rows[at].Parent;
        foreach (var row in rows)
        {
            if (row.Parent == parent)
            {
                yield return row;
            }
        }
    }

    /// <summary>The keys of every node down to <paramref name="depth" />, for a tree that opens partly expanded.</summary>
    /// <remarks>
    ///     Walked over the app's own structure rather than over the rows, because the rows are what this decides: nothing
    ///     is expanded yet the first time a tree renders.
    /// </remarks>
    internal static HashSet<TKey> KeysToDepth<T, TKey>(
        IEnumerable<T>? roots, Func<T, TKey> key, Func<T, IEnumerable<T>?>? children, int depth)
        where TKey : notnull
    {
        var keys = new HashSet<TKey>();
        if (depth <= 0 || children is null)
        {
            return keys;
        }

        Walk(AsList(roots), 1);
        return keys;

        void Walk(IReadOnlyList<T> nodes, int level)
        {
            foreach (var node in nodes)
            {
                var k = key(node);
                if (!keys.Add(k))
                {
                    continue;
                }

                if (level < depth)
                {
                    Walk(AsList(children(node)), level + 1);
                }
            }
        }
    }

    private static IReadOnlyList<T> AsList<T>(IEnumerable<T>? items) =>
        items as IReadOnlyList<T> ?? (items is null ? [] : [.. items]);
}
