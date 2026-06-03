namespace Rask.Core.Routing;

public sealed class RouteBindException : Exception
{
    public RouteBindException(string message) : base(message) { }
    public RouteBindException(string message, Exception inner) : base(message, inner) { }
}
