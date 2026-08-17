using System.Collections.Concurrent;
using System.Reflection;

namespace Rask.Core.Routing;

internal static class RouteTemplateResolver
{
    private static readonly ConcurrentDictionary<Type, string> _cache = new();

    public static string GetLocalTemplate(Type pageType)
    {
        // A Page declares its template in a Route override, which the generator reads at compile time and
        // bakes into the registry — there is nothing on the type to reflect over, so the registry is the
        // source of truth. Deliberately NOT cached: hot reload Replace()s an assembly's registrations to pick
        // up an edited template, and a cached answer here would keep handing back the old one.
        if (RouteRegistry.TryGetLocalTemplate(pageType, out var registered))
        {
            return registered;
        }

        // Legacy [Route] fallback. Immutable for the life of the process, so it stays cached.
        return _cache.GetOrAdd(pageType, static t =>
        {
            // First [Route] declared wins — Route<T>() resolves a single canonical template,
            // so for multi-route pages we expose the first attribute order matches the
            // generator-emitted `Routes.{Type}()` URL formatter, which also picks the first.
            var local = t.GetCustomAttributes<RouteAttribute>(false).FirstOrDefault()
                        ?? throw new InvalidOperationException(
                            $"Route<{t.Name}>() requires '{t.FullName}' to be a routed page — derive it from "
                            + "Page and override Route, and make sure its assembly's generated route registry "
                            + "has run (it registers via a module initializer).");
            return local.Template;
        });
    }
}
