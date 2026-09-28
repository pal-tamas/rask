namespace Rask.Core.Routing;

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
            // Catch-all leaves (e.g. "{**rest}") always rank LAST so explicit routes
            // win even when literal-segment count ties — otherwise a "/" + "/{**rest}"
            // pair would put the catch-all ahead of the home page via the length tie-break.
            var byCatchAll = a.HasCatchAll.CompareTo(b.HasCatchAll);
            if (byCatchAll != 0)
            {
                return byCatchAll;
            }

            var byLiteral = b.LiteralSegmentCount.CompareTo(a.LiteralSegmentCount);
            if (byLiteral != 0)
            {
                return byLiteral;
            }

            return b.FullTemplate.Length.CompareTo(a.FullTemplate.Length);
        });
        return leaves;
    }

    private static void Walk(Route node, string parentTemplate, Type[] parentChain, List<RouteLeaf> leaves)
    {
        var fullTemplate = Combine(parentTemplate, node.Template);
        var chain = new Type[parentChain.Length + 1];
        parentChain.CopyTo(chain, 0);

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
