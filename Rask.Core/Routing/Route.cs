namespace Rask.Core.Routing;

public sealed record Route(Type PageType, string Template, IReadOnlyList<Route>? SubRoutes = null);
