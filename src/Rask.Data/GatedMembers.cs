using System.Collections.Concurrent;

namespace Rask.Data;

/// <summary>
///     The members each entity's <c>Configure</c> gated, and the gate each is behind.
/// </summary>
/// <remarks>
///     Kept beside the model rather than in it: a gate is the app's own value — an enum member — which no migration's
///     snapshot can write as an annotation.
/// </remarks>
internal static class GatedMembers
{
    private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, object>> Declared = new();

    /// <summary>Records that <paramref name="member" /> of <paramref name="entity" /> is behind <paramref name="gate" />.</summary>
    internal static void Remember(Type entity, string member, object gate) =>
        Declared.GetOrAdd(entity, static _ => new(StringComparer.Ordinal))[member] = gate;

    /// <summary>Whether any entity gated anything: a save of a model with none asks nothing.</summary>
    internal static bool Any => !Declared.IsEmpty;

    /// <summary>The members <paramref name="entity" /> gated, with their gates, or none.</summary>
    internal static IEnumerable<KeyValuePair<string, object>> Of(Type entity) =>
        Declared.TryGetValue(entity, out var members) ? members : Array.Empty<KeyValuePair<string, object>>();
}
