namespace Rask.Core.Routing;

public sealed class RouteState
{
    public string Path { get; set; } = "/";
    public IQueryCollection Query { get; set; } = QueryCollection.Empty;
}
