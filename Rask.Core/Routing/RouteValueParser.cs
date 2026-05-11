using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;

namespace Rask.Core.Routing;

internal static class RouteValueParser
{
    private static readonly ConcurrentDictionary<Type, Func<string, (bool ok, object? value)>?> _cache = new();

    private static readonly MethodInfo _genericParse = typeof(RouteValueParser)
        .GetMethod(nameof(ParseTyped), BindingFlags.Static | BindingFlags.NonPublic)!;

    public static bool TryParse(Type targetType, string raw, out object? value)
    {
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlying == typeof(string))
        {
            value = raw;
            return true;
        }

        var parser = _cache.GetOrAdd(underlying, BuildParser);
        if (parser is null)
        {
            value = null;
            return false;
        }

        var (ok, parsed) = parser(raw);
        value = parsed;
        return ok;
    }

    private static Func<string, (bool ok, object? value)>? BuildParser(Type type)
    {
        var implementsIParsable = type.GetInterfaces().Any(i =>
            i.IsGenericType
            && i.GetGenericTypeDefinition() == typeof(IParsable<>)
            && i.GenericTypeArguments[0] == type);
        if (!implementsIParsable)
        {
            return null;
        }

        var typed = _genericParse.MakeGenericMethod(type);
        return raw =>
        {
            var args = new object?[] { raw };
            return ((bool ok, object? value))typed.Invoke(null, args)!;
        };
    }

    private static (bool ok, object? value) ParseTyped<T>(string raw) where T : IParsable<T>
    {
        if (T.TryParse(raw, CultureInfo.InvariantCulture, out var parsed))
        {
            return (true, parsed);
        }

        return (false, null);
    }
}
