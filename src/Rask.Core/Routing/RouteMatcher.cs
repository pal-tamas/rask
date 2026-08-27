namespace Rask.Core.Routing;

internal static class RouteMatcher
{
    public static bool TryMatch(
        IReadOnlyList<RouteLeaf> leaves,
        string path,
        out IReadOnlyList<Type> chain,
        out IReadOnlyDictionary<string, string?> values)
        => TryMatch(leaves, path, out chain, out values, out _);

    /// <summary>
    ///     Match, additionally reporting the winning leaf's full template. The template is how a
    ///     caller tells the not-found page from a page that merely renders like one: the catch-all
    ///     is registered under a reserved template rather than being flagged on the type.
    /// </summary>
    public static bool TryMatch(
        IReadOnlyList<RouteLeaf> leaves,
        string path,
        out IReadOnlyList<Type> chain,
        out IReadOnlyDictionary<string, string?> values,
        out string fullTemplate)
    {
        for (var i = 0; i < leaves.Count; i++)
        {
            var leaf = leaves[i];
            if (leaf.Pattern.TryMatch(path, out var attempt))
            {
                chain = leaf.Chain;
                values = attempt;
                fullTemplate = leaf.FullTemplate;
                return true;
            }
        }

        chain = Array.Empty<Type>();
        values = new Dictionary<string, string?>();
        fullTemplate = string.Empty;
        return false;
    }
}
