namespace Rask.Core.Routing;

public readonly record struct RouteRegistration(Type PageType, string Template, Type? Parent);
