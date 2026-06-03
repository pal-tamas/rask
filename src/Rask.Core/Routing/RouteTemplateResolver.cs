using System.Collections.Concurrent;
using System.Reflection;

namespace Rask.Core.Routing;

internal static class RouteTemplateResolver
{
    private static readonly ConcurrentDictionary<Type, string> _cache = new();

    public static string GetLocalTemplate(Type pageType)
    {
        return _cache.GetOrAdd(pageType, static t =>
        {
            // First [Route] declared wins — Route<T>() resolves a single canonical template,
            // so for multi-route pages we expose the first attribute order matches the
            // generator-emitted `Routes.{Type}()` URL formatter, which also picks the first.
            var local = t.GetCustomAttributes<RouteAttribute>(false).FirstOrDefault()
                        ?? throw new InvalidOperationException(
                            $"Route<{t.Name}>() requires '{t.FullName}' to be annotated with [Route(\"...\")].");
            return local.Template;
        });
    }
}
