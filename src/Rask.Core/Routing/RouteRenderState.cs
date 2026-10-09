namespace Rask.Core.Routing;

internal sealed class RouteRenderState
{
    public RouteRenderState(string path, IReadOnlyList<Type> chain, IReadOnlyDictionary<string, string?> values,
        IQueryCollection query)
    {
        Path = path;
        Chain = chain;
        Values = values;
        Query = query;
        Cursor = 0;
    }

    public string Path { get; }
    public IReadOnlyList<Type> Chain { get; }
    public IReadOnlyDictionary<string, string?> Values { get; }
    public IQueryCollection Query { get; }
    public int Cursor { get; set; }

    /// <summary>
    ///     Where this route's pages offer their title, or <c>null</c> when they do not name the page: only the
    ///     first router of a walk does. A router rendered inside a page of another one shows pages of its own
    ///     and leaves the title alone.
    /// </summary>
    public RouteTitle? Title { get; init; }

    /// <summary>The router's page instances, by place in the chain.</summary>
    public required RoutePageInstances Pages { get; init; }
}
