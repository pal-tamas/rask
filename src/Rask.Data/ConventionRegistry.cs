using System.Collections.Concurrent;
using System.ComponentModel;

namespace Rask.Data;

/// <summary>
///     Which <see cref="Timestamps" /> each entity type asked for, as the generator read them.
/// </summary>
/// <remarks>
///     <para>
///         Populated by generated <c>[ModuleInitializer]</c> code, and consulted by
///         <see cref="ModelBuilderExtensions.ApplyRaskConventions(Microsoft.EntityFrameworkCore.ModelBuilder)" /> when it decides whether to add
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
    private static readonly ConcurrentDictionary<Type, List<ValueCollection>> DeclaredCollections = new();
    private static readonly ConcurrentDictionary<Type, Tenancy> DeclaredScopes = new();
    private static readonly ConcurrentDictionary<Type, Broadcasts> DeclaredBroadcasts = new();

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
    /// <param name="broadcasts">The value of its <c>Broadcast</c> const.</param>
    public static void Declare(Type entity, Broadcasts broadcasts)
    {
        ArgumentNullException.ThrowIfNull(entity);
        DeclaredBroadcasts[entity] = broadcasts;
    }

    /// <summary>Records what <paramref name="entity" /> declared. Called by generated code.</summary>
    /// <param name="entity">The entity type.</param>
    /// <param name="checks">The value of its <c>Checks</c> const.</param>
    public static void Declare(Type entity, Concurrency checks)
    {
        ArgumentNullException.ThrowIfNull(entity);
        DeclaredChecks[entity] = checks;
    }

    /// <summary>Records what <paramref name="entity" /> declared. Called by generated code.</summary>
    /// <param name="entity">The entity type.</param>
    /// <param name="scope">The value of its <c>Scope</c> const, or its aggregate root&apos;s for a child.</param>
    public static void Declare(Type entity, Tenancy scope)
    {
        ArgumentNullException.ThrowIfNull(entity);
        DeclaredScopes[entity] = scope;
    }

    /// <summary>Whether <paramref name="entity" /> is partitioned by tenant. It is not unless it asked.</summary>
    /// <param name="entity">The entity type.</param>
    internal static Tenancy ScopeFor(Type entity) =>
        DeclaredScopes.TryGetValue(entity, out var scope) ? scope : Tenancy.Shared;

    /// <summary>Whether any entity at all has asked to be partitioned by tenant.</summary>
    internal static bool AnyTenantScoped => !DeclaredScopes.IsEmpty && DeclaredScopes.Values.Any(static s => s == Tenancy.PerTenant);

    /// <summary>
    ///     Records a collection of values <paramref name="entity" /> holds, for
    ///     <see cref="ModelBuilderExtensions.ApplyRaskConventions(Microsoft.EntityFrameworkCore.ModelBuilder)" /> to map. Called by generated code.
    /// </summary>
    /// <param name="entity">The entity type holding the collection.</param>
    /// <param name="property">The property's name — <c>Tags</c>, <c>Stops</c>.</param>
    /// <param name="field">
    ///     The backing field generated code writes through, or null when the property itself is writable.
    /// </param>
    /// <param name="element">
    ///     The value object's type when the collection holds value objects, which makes it a JSON column;
    ///     null when it holds plain values, which makes it a primitive collection.
    /// </param>
    /// <remarks>
    ///     Like the consts above, the answer is settled at compile time rather than reflected over: deciding
    ///     at runtime whether an element type is a value object would mean a second implementation of a rule
    ///     the generator already applies, free to disagree with it.
    /// </remarks>
    public static void DeclareCollection(Type entity, string property, string? field, Type? element)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentException.ThrowIfNullOrEmpty(property);

        DeclaredCollections.GetOrAdd(entity, static _ => []).Add(new ValueCollection(property, field, element));
    }

    /// <summary>The collections of values <paramref name="entity" /> declared, or none.</summary>
    /// <param name="entity">The entity type.</param>
    internal static IReadOnlyList<ValueCollection> CollectionsFor(Type entity) =>
        DeclaredCollections.TryGetValue(entity, out var collections) ? collections : [];

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

    /// <summary>Whether a save of <paramref name="entity" /> is announced process-wide. It is not unless it asked.</summary>
    /// <param name="entity">The entity type.</param>
    internal static Broadcasts BroadcastsFor(Type entity) =>
        DeclaredBroadcasts.TryGetValue(entity, out var broadcasts) ? broadcasts : Broadcasts.Never;
}

/// <summary>One collection of values an entity holds, as the generator read it.</summary>
/// <param name="Property">The property's name.</param>
/// <param name="Field">The backing field to write through, or null for a writable property.</param>
/// <param name="Element">The value object's type for a JSON collection; null for a primitive one.</param>
internal readonly record struct ValueCollection(string Property, string? Field, Type? Element);
