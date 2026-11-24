using System.Collections.Concurrent;

namespace Rask.Data;

/// <summary>
///     The entity types of Rask's own batteries, whose tables and columns keep their names under the app's
///     <c>TableNames</c> and <c>ColumnNames</c>: their migrations, raw SQL and runbooks already name them.
/// </summary>
/// <remarks>
///     Recorded by each battery's own configuration as the model is built, and kept OUT of the model: an annotation would
///     travel into every app's model snapshot.
/// </remarks>
internal static class KeptNames
{
    private static readonly ConcurrentDictionary<Type, byte> Types = new();

    /// <summary>Records that <paramref name="entity" /> keeps its names.</summary>
    internal static void Keep(Type entity) => Types.TryAdd(entity, 0);

    /// <summary>Whether a battery recorded <paramref name="entity" />.</summary>
    internal static bool Has(Type entity) => Types.ContainsKey(entity);
}
