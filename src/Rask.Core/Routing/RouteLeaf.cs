namespace Rask.Core.Routing;

internal sealed class RouteLeaf
{
    public RouteLeaf(string fullTemplate, IReadOnlyList<Type> chain, RoutePattern pattern, int literalSegmentCount)
    {
        FullTemplate = fullTemplate;
        Chain = chain;
        Pattern = pattern;
        LiteralSegmentCount = literalSegmentCount;
        HasCatchAll = pattern.Segments.Any(s => s.Kind == SegmentKind.CatchAll);
    }

    public string FullTemplate { get; }
    public IReadOnlyList<Type> Chain { get; }
    public RoutePattern Pattern { get; }
    public int LiteralSegmentCount { get; }
    public bool HasCatchAll { get; }
}
