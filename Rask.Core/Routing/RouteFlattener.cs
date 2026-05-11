namespace Rask.Core.Routing;

internal sealed class RouteLeaf
{
    public RouteLeaf(string fullTemplate, IReadOnlyList<Type> chain, RoutePattern pattern, int literalSegmentCount)
    {
        FullTemplate = fullTemplate;
        Chain = chain;
        Pattern = pattern;
        LiteralSegmentCount = literalSegmentCount;
    }

    public string FullTemplate { get; }
    public IReadOnlyList<Type> Chain { get; }
    public RoutePattern Pattern { get; }
    public int LiteralSegmentCount { get; }
}

internal static class RouteFlattener
{
    public static IReadOnlyList<RouteLeaf> Flatten(IEnumerable<Route> roots)
    {
        var leaves = new List<RouteLeaf>();
        foreach (var root in roots)
        {
            Walk(root, "", Array.Empty<Type>(), leaves);
        }

        leaves.Sort((a, b) =>
        {
            var byLiteral = b.LiteralSegmentCount.CompareTo(a.LiteralSegmentCount);
            if (byLiteral != 0)
            {
                return byLiteral;
            }

            return b.FullTemplate.Length.CompareTo(a.FullTemplate.Length);
        });
        return leaves;
    }

    private static void Walk(Route node, string parentTemplate, IReadOnlyList<Type> parentChain, List<RouteLeaf> leaves)
    {
        var fullTemplate = Combine(parentTemplate, node.Template);
        var chain = new Type[parentChain.Count + 1];
        for (var i = 0; i < parentChain.Count; i++)
        {
            chain[i] = parentChain[i];
        }

        chain[^1] = node.PageType;

        if (node.SubRoutes is null || node.SubRoutes.Count == 0)
        {
            leaves.Add(BuildLeaf(fullTemplate, chain));
            return;
        }

        foreach (var sub in node.SubRoutes)
        {
            Walk(sub, fullTemplate, chain, leaves);
        }
    }

    internal static string Combine(string parent, string child)
    {
        var c = child.Trim('/');
        if (c.Length == 0)
        {
            return parent.Length == 0 ? "/" : parent;
        }

        var basePart = parent.Length == 0 ? string.Empty : parent.TrimEnd('/');
        return basePart + "/" + c;
    }

    private static RouteLeaf BuildLeaf(string template, IReadOnlyList<Type> chain)
    {
        var pattern = RoutePattern.Parse(template);
        return new RouteLeaf(template, chain, pattern, pattern.LiteralSegmentCount);
    }
}
