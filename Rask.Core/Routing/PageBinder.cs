using System.Collections.Concurrent;
using System.Reflection;

namespace Rask.Core.Routing;

internal static class PageBinder
{
    private static readonly ConcurrentDictionary<Type, BindablePropertyInfo[]> _propertyCache = new();

    public static void Bind(Component page, IReadOnlyDictionary<string, string?> values, IQueryCollection query)
    {
        var properties = _propertyCache.GetOrAdd(page.GetType(), DiscoverProperties);

        foreach (var property in properties)
        {
            if (TryGetRawValue(property, values, query, out var raw))
            {
                var converted = ConvertValue(raw, property.Property.PropertyType, property.Property.Name);
                property.Property.SetValue(page, converted);
            }
        }
    }

    private static BindablePropertyInfo[] DiscoverProperties(Type type)
    {
        var result = new List<BindablePropertyInfo>();
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanWrite || p.GetIndexParameters().Length != 0)
            {
                continue;
            }

            var routeParam = p.GetCustomAttribute<RouteParamAttribute>(false);
            var queryParam = p.GetCustomAttribute<QueryParamAttribute>(false);
            if (routeParam is null && queryParam is null)
            {
                continue;
            }

            result.Add(new BindablePropertyInfo(
                p,
                routeParam is not null,
                queryParam is not null,
                routeParam?.Name ?? queryParam?.Name ?? p.Name));
        }

        return result.ToArray();
    }

    private static bool TryGetRawValue(BindablePropertyInfo info, IReadOnlyDictionary<string, string?> values,
        IQueryCollection query, out object? raw)
    {
        if (info.IsRouteParam)
        {
            foreach (var (key, value) in values)
            {
                if (string.Equals(key, info.BindingName, StringComparison.OrdinalIgnoreCase))
                {
                    raw = value;
                    return true;
                }
            }
        }

        if (info.IsQueryParam)
        {
            foreach (var key in query.Keys)
            {
                if (string.Equals(key, info.BindingName, StringComparison.OrdinalIgnoreCase))
                {
                    var stringValues = query[key];
                    raw = stringValues.Count == 0 ? null : stringValues[0];
                    return true;
                }
            }
        }

        raw = null;
        return false;
    }

    private static object? ConvertValue(object? raw, Type targetType, string propertyName)
    {
        if (raw is null)
        {
            return null;
        }

        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (raw.GetType() == underlying)
        {
            return raw;
        }

        var asString = raw as string ?? raw.ToString();
        if (asString is null)
        {
            return null;
        }

        if (RouteValueParser.TryParse(targetType, asString, out var value))
        {
            return value;
        }

        throw new RouteBindException(
            $"Failed to bind value '{asString}' to property '{propertyName}' of type '{targetType}'.");
    }

    private readonly record struct BindablePropertyInfo(
        PropertyInfo Property,
        bool IsRouteParam,
        bool IsQueryParam,
        string BindingName);
}
