using System.Collections.Concurrent;
using System.ComponentModel;

namespace Rask.Data;

/// <summary>
///     Which <see cref="Timestamps" /> each entity type asked for, as the generator read them.
/// </summary>
/// <remarks>
///     <para>
///         Populated by generated <c>[ModuleInitializer]</c> code, and consulted by
///         <see cref="ModelBuilderExtensions.ApplyRaskConventions" /> when it decides whether to add
///         <c>CreatedAt</c> and <c>UpdatedAt</c>.
///     </para>
///     <para>
///         It exists because the answer cannot be reflected over. <c>Stamps</c> is a <c>const</c>, so every use
///         is inlined and the field itself is free to be trimmed — a runtime <c>GetField</c> would find nothing
///         in a trimmed publish and fall back to <see cref="Timestamps.All" />, giving an app one shape in
///         debug and another in release. Read at COMPILE time and registered here, the answer travels as
///         generated code that nothing can trim away.
///     </para>
///     <para>
///         An entity that registers nothing gets <see cref="Timestamps.All" />, so a type that says nothing is
///         unchanged.
///     </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ConventionRegistry
{
    private static readonly ConcurrentDictionary<Type, Timestamps> DeclaredStamps = new();
    private static readonly ConcurrentDictionary<Type, Deletion> DeclaredDeletes = new();
    private static readonly ConcurrentDictionary<Type, Concurrency> DeclaredChecks = new();

    /// <summary>Records what <paramref name="entity" /> declared. Called by generated code.</summary>
    /// <param name="entity">The entity type.</param>
    /// <param name="stamps">The value of its <c>Stamps</c> const.</param>
    public static void Declare(Type entity, Timestamps stamps)
    {
        ArgumentNullException.ThrowIfNull(entity);
        DeclaredStamps[entity] = stamps;
    }

    /// <summary>Records what <paramref name="entity" /> declared. Called by generated code.</summary>
    /// <param name="entity">The entity type.</param>
    /// <param name="deletes">The value of its <c>Deletes</c> const.</param>
    public static void Declare(Type entity, Deletion deletes)
    {
        ArgumentNullException.ThrowIfNull(entity);
        DeclaredDeletes[entity] = deletes;
    }

    /// <summary>Records what <paramref name="entity" /> declared. Called by generated code.</summary>
    /// <param name="entity">The entity type.</param>
    /// <param name="checks">The value of its <c>Checks</c> const.</param>
    public static void Declare(Type entity, Concurrency checks)
    {
        ArgumentNullException.ThrowIfNull(entity);
        DeclaredChecks[entity] = checks;
    }

    /// <summary>What <paramref name="entity" /> asked for, or <see cref="Timestamps.All" /> when it did not ask.</summary>
    /// <param name="entity">The entity type.</param>
    internal static Timestamps StampsFor(Type entity) =>
        DeclaredStamps.TryGetValue(entity, out var stamps) ? stamps : Timestamps.All;

    /// <summary>How <paramref name="entity" /> deletes. HARD unless it asked for soft.</summary>
    /// <param name="entity">The entity type.</param>
    internal static Deletion DeletesFor(Type entity) =>
        DeclaredDeletes.TryGetValue(entity, out var deletes) ? deletes : Deletion.Hard;

    /// <summary>Whether <paramref name="entity" /> carries a version token. It does unless it declined.</summary>
    /// <param name="entity">The entity type.</param>
    internal static Concurrency ChecksFor(Type entity) =>
        DeclaredChecks.TryGetValue(entity, out var checks) ? checks : Concurrency.Version;
}
