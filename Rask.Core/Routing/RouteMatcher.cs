namespace Rask.Core.Routing;

internal static class RouteMatcher
{
    public static bool TryMatch(
        IReadOnlyList<RouteLeaf> leaves,
        string path,
        out IReadOnlyList<Type> chain,
        out IReadOnlyDictionary<string, string?> values)
    {
        for (var i = 0; i < leaves.Count; i++)
        {
            var leaf = leaves[i];
            if (leaf.Pattern.TryMatch(path, out var attempt))
            {
                chain = leaf.Chain;
                values = attempt;
                return true;
            }
        }

        chain = Array.Empty<Type>();
        values = new Dictionary<string, string?>();
        return false;
    }
}
