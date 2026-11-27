namespace Rask.Core.Routing;

internal static class RouteTemplateResolver
{
    public static string GetLocalTemplate(Type pageType)
    {
        // The generator reads every [Route] at compile time and bakes it into the registry, so the registry is
        // the one source of truth — nothing reflects over the attribute. Deliberately NOT cached: hot reload
        // Replace()s an assembly's registrations to pick up an edited template, and a cached answer here would
        // keep handing back the old one.
        if (RouteRegistry.TryGetLocalTemplate(pageType, out var registered))
        {
            return registered;
        }

        throw new InvalidOperationException(
            $"Route.To<{pageType.Name}>() requires '{pageType.FullName}' to be a routed page — give it "
            + "[Route(\"/…\")], and make sure its assembly's generated route registry "
            + "has run (it registers via a module initializer).");
    }
}
