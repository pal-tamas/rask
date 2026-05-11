namespace Rask.Core.Routing;

internal sealed class RouteRenderState
{
    public RouteRenderState(IReadOnlyList<Type> chain, IReadOnlyDictionary<string, string?> values,
        IQueryCollection query)
    {
        Chain = chain;
        Values = values;
        Query = query;
        Cursor = 0;
    }

    public IReadOnlyList<Type> Chain { get; }
    public IReadOnlyDictionary<string, string?> Values { get; }
    public IQueryCollection Query { get; }
    public int Cursor { get; set; }
}
