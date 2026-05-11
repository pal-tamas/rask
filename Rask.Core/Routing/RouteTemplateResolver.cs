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
            var local = t.GetCustomAttribute<RouteAttribute>(false)
                        ?? throw new InvalidOperationException(
                            $"Route<{t.Name}>() requires '{t.FullName}' to be annotated with [Route(\"...\")].");
            return local.Template;
        });
    }
}
