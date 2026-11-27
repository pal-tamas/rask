using System.Collections.Concurrent;

namespace Rask.Data;

/// <summary>
///     The entity types of Rask's own batteries (jobs, mail, outbox, storage, auth, …): infrastructure, which no
///     aggregate's rules reach.
/// </summary>
/// <remarks>
///     Recorded by each battery's own configuration as the model is built, and kept OUT of the model: an annotation would
///     travel into every app's model snapshot. Their tables and columns are named as every other table is.
/// </remarks>
internal static class BatteryEntities
{
    private static readonly ConcurrentDictionary<Type, byte> Types = new();

    /// <summary>Records that <paramref name="entity" /> is a battery's.</summary>
    internal static void Record(Type entity) => Types.TryAdd(entity, 0);

    /// <summary>Whether a battery recorded <paramref name="entity" />.</summary>
    internal static bool Has(Type entity) => Types.ContainsKey(entity);
}
