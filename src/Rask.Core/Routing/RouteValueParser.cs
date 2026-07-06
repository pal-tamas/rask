using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Rask.Core.Routing;

internal static class RouteValueParser
{
    // Memoises the dynamic (reflection) parser per custom type. Only populated on the interpreter
    // path — the reflection-free TypedParserRegistry handles every pre-registered type first.
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

        // Registry first: every BCL IParsable primitive and every generator-/app-registered custom
        // type resolves here with no runtime code generation, so a full-AOT build never needs the
        // dynamic fallback below.
        if (TypedParserRegistry.TryGet(underlying, out var registered))
        {
            var (ok, parsed) = registered(raw);
            value = parsed;
            return ok;
        }

        // Registry miss: fall back to a reflection-built parser. BuildParser returns null when the
        // runtime can't generate code (a full-AOT build) or the type isn't IParsable, so an
        // unregistered type simply fails to bind (routes surface RouteBindException; forms no-op) —
        // never a throw, which would regress working interpreter apps. The IsDynamicCodeSupported
        // guard lives inside BuildParser, next to the MakeGenericMethod call it protects.
        var parser = _cache.GetOrAdd(underlying, BuildParser);
        if (parser is not null)
        {
            var (ok, parsed) = parser(raw);
            value = parsed;
            return ok;
        }

        value = null;
        return false;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2060",
        Justification = "MakeGenericMethod over ParseTyped<T> only fires for types that implement IParsable<T>. " +
                        "The Roslyn analyser (RASK011) refuses to generate code that would feed a non-IParsable " +
                        "type into this path, and the public TryParse method on the concrete type is preserved " +
                        "alongside the page properties via the generated [DynamicDependency] entries.")]
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "GetInterfaces is called to probe for IParsable<T>; the page-property type is reached " +
                        "from a routed page whose members are preserved via [DynamicDependency].")]
    private static Func<string, (bool ok, object? value)>? BuildParser(Type type)
    {
        // Guarded so the MakeGenericMethod usage (RequiresDynamicCode/IL3050) is recognised as
        // unreachable under AOT — no suppression needed. Only reached from the guarded call site
        // above, but the guard is repeated here so the trimmer analyses this method in isolation.
        if (RuntimeFeature.IsDynamicCodeSupported)
        {
            var implementsIParsable = type.GetInterfaces().Any(i =>
                i.IsGenericType
                && i.GetGenericTypeDefinition() == typeof(IParsable<>)
                && i.GenericTypeArguments[0] == type);
            if (implementsIParsable)
            {
                var typed = _genericParse.MakeGenericMethod(type);
                return raw =>
                {
                    var args = new object?[] { raw };
                    return ((bool ok, object? value))typed.Invoke(null, args)!;
                };
            }
        }

        return null;
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
