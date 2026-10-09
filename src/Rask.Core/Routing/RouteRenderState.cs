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

    /// <summary>
    ///     The router's page instances, by place in the chain — or <c>null</c> for a path nothing matched,
    ///     which has no pages and is here only to say that the page has no title.
    /// </summary>
    public RoutePageInstances? Pages { get; init; }

    /// <summary>The route of the router this one is rendered inside, when there is one.</summary>
    public RouteRenderState? Outer { get; init; }

    /// <summary>The state of a walk whose first router matched nothing.</summary>
    public static RouteRenderState Unmatched(string path, RouteTitle title) =>
        new(path, [], System.Collections.ObjectModel.ReadOnlyDictionary<string, string?>.Empty, QueryCollection.Empty)
        {
            Title = title,
        };
}
