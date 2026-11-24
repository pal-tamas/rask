using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace Rask.Data;

/// <summary>
///     Every <c>b.Projects&lt;E&gt;(…)</c> the process declared, by the row type it is on — kept beside the model, as
///     <see cref="ParentLocks" /> are, because a declaration holds delegates no migration snapshot could write.
/// </summary>
internal static class DeclaredProjections
{
    private static readonly ConcurrentDictionary<Type, ImmutableArray<DeclaredProjection>> ByType = new();

    // Read on every save: a process that declares no projection pays one field read for this whole feature.
    private static volatile bool _any;

    /// <summary>Whether any type in the process declares a projection.</summary>
    internal static bool Any => _any;

    /// <summary>Records <paramref name="projection" /> on <paramref name="type" />, in place of one raising the same event.</summary>
    internal static void Add(Type type, DeclaredProjection projection)
    {
        ByType.AddOrUpdate(
            type,
            static (_, projection) => [projection],
            static (_, held, projection) => [.. held.Where(p => p.EventType != projection.EventType), projection],
            projection);
        _any = true;
    }

    /// <summary>The projections <paramref name="type" /> declares; empty when it declares none.</summary>
    internal static ImmutableArray<DeclaredProjection> Of(Type type) => ByType.GetValueOrDefault(type, []);
}
