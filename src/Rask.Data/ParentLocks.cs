using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace Rask.Data;

/// <summary>
///     Each type's declared locks (<see cref="LockedByBuilderExtensions" />), recorded as its <c>Configure</c> runs and
///     asked by the save's domain step (<see cref="DomainValidation" />).
/// </summary>
/// <remarks>
///     Kept OUT of the model: a lambda is no annotation a model snapshot can write. A model built again records the same
///     locks again, each in place of itself — one per parent property.
/// </remarks>
internal static class ParentLocks
{
    private static readonly ConcurrentDictionary<Type, ImmutableArray<ParentLock>> ByType = new();

    // Read on every save: a process that declares no lock pays one field read for this whole feature.
    private static volatile bool _any;

    /// <summary>Whether any type in the process declares a lock.</summary>
    internal static bool Any => _any;

    /// <summary>Records <paramref name="rule" /> on <paramref name="type" />, in place of one on the same property.</summary>
    internal static void Add(Type type, ParentLock rule)
    {
        ByType.AddOrUpdate(
            type,
            static (_, rule) => [rule],
            static (_, held, rule) => [.. held.Where(r => !string.Equals(r.Property, rule.Property, StringComparison.Ordinal)), rule],
            rule);
        _any = true;
    }

    /// <summary>The locks <paramref name="type" /> declares; empty when it declares none.</summary>
    internal static ImmutableArray<ParentLock> Of(Type type) => ByType.GetValueOrDefault(type, []);
}
