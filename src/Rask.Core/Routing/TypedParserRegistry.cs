using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;

namespace Rask.Core.Routing;

/// <summary>
///     A reflection-free, AOT-safe map from a value type to a delegate that parses a string into it
///     via <see cref="IParsable{TSelf}" />. Seeded at static-init with every BCL <c>IParsable</c>
///     primitive; each seed is a <em>closed</em> generic instantiation the AOT compiler sees, so no
///     runtime code generation (<c>MakeGenericMethod</c>) is needed for the common case.
///     <para>
///         Custom <c>IParsable</c> value types are auto-registered for routed pages by the routes
///         generator (per <c>[RouteParam]</c>/<c>[QueryParam]</c> property); form-model custom value
///         types are registered by the app via <see cref="Forms.RaskBinding.RegisterParsable{T}" />.
///         <see cref="RouteValueParser" /> consults this registry first and only falls back to the
///         dynamic <c>MakeGenericMethod</c> path when runtime code generation is available (the Mono
///         interpreter), so an interp-free AOT build stays entirely within this registry.
///     </para>
/// </summary>
internal static class TypedParserRegistry
{
    private static readonly ConcurrentDictionary<Type, Func<string, (bool ok, object? value)>> Map = new();

    static TypedParserRegistry()
    {
        // Integers (incl. native-sized and 128-bit) and arbitrary-precision.
        Register<byte>();
        Register<sbyte>();
        Register<short>();
        Register<ushort>();
        Register<int>();
        Register<uint>();
        Register<long>();
        Register<ulong>();
        Register<nint>();
        Register<nuint>();
        Register<Int128>();
        Register<UInt128>();
        Register<BigInteger>();

        // Floating point.
        Register<Half>();
        Register<float>();
        Register<double>();
        Register<decimal>();

        // Other scalars.
        Register<bool>();
        Register<char>();
        Register<Guid>();

        // Date / time.
        Register<DateTime>();
        Register<DateTimeOffset>();
        Register<DateOnly>();
        Register<TimeOnly>();
        Register<TimeSpan>();
    }

    /// <summary>
    ///     Registers a parser for <typeparamref name="T" />. Idempotent; the last registration for a
    ///     given type wins. Called from the routes generator's module initializer and from
    ///     <see cref="Forms.RaskBinding.RegisterParsable{T}" />.
    /// </summary>
    internal static void Register<T>() where T : IParsable<T> =>
        Map[typeof(T)] = static raw =>
            T.TryParse(raw, CultureInfo.InvariantCulture, out var parsed)
                ? (true, (object?)parsed)
                : (false, null);

    /// <summary>Looks up the parser for <paramref name="type" /> (already unwrapped of nullability).</summary>
    internal static bool TryGet(Type type, [NotNullWhen(true)] out Func<string, (bool ok, object? value)>? parser) =>
        Map.TryGetValue(type, out parser);
}
