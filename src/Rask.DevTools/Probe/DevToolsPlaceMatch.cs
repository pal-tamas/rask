using System.Globalization;

namespace Rask.DevTools.Probe;

/// <summary>
///     Which component a node on the page belongs to, found by the places the Tree tab gives every component — the same
///     match the host does when a pick lands on the page (host/anchors.ts), in C#.
/// </summary>
internal static class DevToolsPlaceMatch
{
    /// <summary>A place's parts: the path to its parent, the slot of its first node, and how many nodes it covers.</summary>
    internal readonly record struct Place(int[] Path, int First, int Count);

    /// <summary>Reads <c>path|firstSlot|count</c>; null for anything else.</summary>
    internal static Place? Parse(string? at)
    {
        if (at is null)
        {
            return null;
        }

        var parts = at.Split('|');
        if (parts.Length != 3
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var first)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            || count < 1)
        {
            return null;
        }

        if (parts[0].Length == 0)
        {
            return new Place([], first, count);
        }

        var segments = parts[0].Split('.');
        var path = new int[segments.Length];
        for (var i = 0; i < segments.Length; i++)
        {
            if (!int.TryParse(segments[i], NumberStyles.None, CultureInfo.InvariantCulture, out path[i]))
            {
                return null;
            }
        }

        return new Place(path, first, count);
    }

    /// <summary>Whether the node at <paramref name="nodePath" /> (every slot from the document down) is inside the place.</summary>
    internal static bool Contains(Place place, IReadOnlyList<int> nodePath)
    {
        if (nodePath.Count <= place.Path.Length)
        {
            return false;
        }

        for (var i = 0; i < place.Path.Length; i++)
        {
            if (nodePath[i] != place.Path[i])
            {
                return false;
            }
        }

        var slot = nodePath[place.Path.Length];
        return slot >= place.First && slot < place.First + place.Count;
    }

    /// <summary>
    ///     The innermost component whose nodes hold the node at <paramref name="nodePath" />, and the components around it,
    ///     outermost first — or null when none does. HTML elements are passed through, never matched: the path names
    ///     components.
    /// </summary>
    internal static (DevToolsComponentNode Component, List<string> Path)? Find(DevToolsComponentNode root, IReadOnlyList<int> nodePath)
    {
        var trail = new List<DevToolsComponentNode>();
        (DevToolsComponentNode Component, List<string> Path)? best = null;
        var bestDepth = -1;

        void Walk(DevToolsComponentNode node)
        {
            var isComponent = !node.IsTag;
            if (isComponent)
            {
                trail.Add(node);
            }

            if (isComponent && Parse(node.At) is { } place && Contains(place, nodePath) && place.Path.Length >= bestDepth)
            {
                bestDepth = place.Path.Length;
                best = (node, trail.Select(n => n.Type).Skip(1).ToList());
            }

            foreach (var child in node.Children)
            {
                Walk(child);
            }

            if (isComponent)
            {
                trail.RemoveAt(trail.Count - 1);
            }
        }

        Walk(root);
        return best;
    }
}
