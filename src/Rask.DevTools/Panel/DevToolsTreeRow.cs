using Rask.DevTools.Probe;

namespace Rask.DevTools.Panel;

/// <summary>One visible node of the component tree, with what its row and the keyboard need.</summary>
/// <param name="Node">The node.</param>
/// <param name="Level">1 for the root, one more per generation — what <c>aria-level</c> says.</param>
/// <param name="Parent">The parent's index among the visible rows, or <c>-1</c> for the root.</param>
/// <param name="HasChildren">Whether it can be opened at all.</param>
/// <param name="IsExpanded">Whether its children are the rows below it.</param>
/// <param name="SetSize">How many siblings it has, itself included.</param>
/// <param name="PosInSet">Its 1-based place among them.</param>
internal readonly record struct DevToolsTreeRow(
    DevToolsComponentNode Node,
    int Level,
    int Parent,
    bool HasChildren,
    bool IsExpanded,
    int SetSize,
    int PosInSet)
{
    /// <summary>
    ///     The visible rows, depth first: a node's children follow it only while it is open, so an arrow key moves to the
    ///     next row on screen.
    /// </summary>
    /// <remarks>A node seen twice is listed once, which also ends a cycle.</remarks>
    /// <param name="root">The tree's root.</param>
    /// <param name="expanded">The ids whose children are shown.</param>
    /// <param name="index">Filled with each visible id's row.</param>
    internal static List<DevToolsTreeRow> Visible(
        DevToolsComponentNode root, IReadOnlyCollection<long> expanded, Dictionary<long, int> index)
    {
        var rows = new List<DevToolsTreeRow>();
        Walk([root], 1, -1);
        return rows;

        void Walk(IReadOnlyList<DevToolsComponentNode> siblings, int level, int parent)
        {
            var kept = siblings.Where(node => index.TryAdd(node.Id, -1)).ToList();
            for (var i = 0; i < kept.Count; i++)
            {
                var node = kept[i];
                var hasChildren = node.Children.Count > 0;
                var isExpanded = hasChildren && expanded.Contains(node.Id);

                index[node.Id] = rows.Count;
                rows.Add(new DevToolsTreeRow(node, level, parent, hasChildren, isExpanded, kept.Count, i + 1));

                if (isExpanded)
                {
                    Walk(node.Children, level + 1, rows.Count - 1);
                }
            }
        }
    }
}
