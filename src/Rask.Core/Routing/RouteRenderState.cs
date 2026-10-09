using Rask.Core.Components;

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

    /// <summary>The chain's mounted pages, layout first: what the router and each <see cref="Outlet" /> place in turn.</summary>
    public Component[] Pages { get; set; } = [];

    public int Cursor { get; set; }

    /// <summary>The next page of the chain, or nothing once the leaf has been placed.</summary>
    public Component NextPage() => Cursor < Pages.Length ? Pages[Cursor++] : new Fragment();
}
