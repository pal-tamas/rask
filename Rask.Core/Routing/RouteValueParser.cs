using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;

namespace Rask.Core.Routing;

internal static class RouteValueParser
{
    private static readonly ConcurrentDictionary<Type, Func<string, (bool ok, object? value)>?> _cache = new();

    private static readonly MethodInfo _genericParse = typeof(RouteValueParser)
        .GetMethod(nameof(ParseTyped), BindingFlags.Static | BindingFlags.NonPublic)!;

    [UnconditionalSuppressMessage("Trimming", "IL2067",
        Justification = "targetType is passed to BuildParser via a Func<Type, ...> method-group, which erases " +
                        "the DAM annotation. BuildParser carries explicit suppressions for the same reasons.")]
    public static bool TryParse(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces |
                                    DynamicallyAccessedMemberTypes.PublicMethods)]
        Type targetType,
        string raw,
        out object? value)
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

    [UnconditionalSuppressMessage("Trimming", "IL2060",
        Justification = "MakeGenericMethod over ParseTyped<T> only fires for types that implement IParsable<T>. " +
                        "The Roslyn analyser (RASK011) refuses to generate code that would feed a non-IParsable " +
                        "type into this path, and the public TryParse method on the concrete type is preserved " +
                        "alongside the page properties via the generated [DynamicDependency] entries.")]
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "GetInterfaces is called to probe for IParsable<T>; the page-property type is reached " +
                        "from a routed page whose members are preserved via [DynamicDependency].")]
    [UnconditionalSuppressMessage("Trimming", "IL3050",
        Justification = "AOT-only diagnostic — MakeGenericMethod requires runtime code-gen. The browser-wasm " +
                        "build uses the interpreter (RunAOTCompilation=false), so dynamic generics are supported.")]
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
