using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Rask.Data;

/// <summary>
///     The traits each entity type's base names, as the generator read them — a child of a
///     <see cref="PerTenant" /> root is registered partitioned too: tenancy is the aggregate's.
/// </summary>
/// <remarks>
///     <para>
///         Populated by generated <c>[ModuleInitializer]</c> code, and consulted by
///         <see cref="ModelBuilderExtensions.ApplyRaskConventions(Microsoft.EntityFrameworkCore.ModelBuilder)" />,
///         the interceptors and the generated writes.
///     </para>
///     <para>
///         Read at COMPILE time and registered here, the answer travels as generated code that nothing can trim
///         away, and the generator's rule for a child's tenancy is the one rule there is.
///     </para>
///     <para>
///         An entity that registers nothing carries nothing: no timestamps, no version, no soft delete.
///     </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ConventionRegistry
{
    private static readonly ConcurrentDictionary<Type, TraitSet> DeclaredTraits = new();
    private static readonly ConcurrentDictionary<Type, Type> DeclaredTenantKeys = new();
    private static readonly ConcurrentDictionary<Type, List<ValueCollection>> DeclaredCollections = new();
    private static readonly ConcurrentDictionary<Type, List<ChildField>> DeclaredChildFields = new();

    /// <summary>Records the traits <paramref name="entity" /> named. Called by generated code.</summary>
    /// <param name="entity">The entity type.</param>
    /// <param name="traits">What its base names, and its root's tenancy for a child.</param>
    public static void Declare(Type entity, TraitSet traits)
    {
        ArgumentNullException.ThrowIfNull(entity);
        DeclaredTraits[entity] = traits;
    }

    /// <summary>
    ///     Records the key type of <c>PerTenant&lt;TKey&gt;</c>, for an aggregate that does not declare its
    ///     <c>TenantId</c>. Called by generated code.
    /// </summary>
    /// <param name="entity">The entity type.</param>
    /// <param name="key"><see cref="Guid" />, <see cref="int" /> or <see cref="long" />.</param>
    public static void DeclareTenantKey(Type entity, Type key)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(key);
        DeclaredTenantKeys[entity] = key;
    }

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
    ///     Settled at compile time rather than reflected over: deciding at runtime whether an element type is a
    ///     value object would mean a second implementation of a rule the generator already applies, free to
    ///     disagree with it.
    /// </remarks>
    public static void DeclareCollection(
        Type entity, string property, string? field, [DynamicallyAccessedMembers(DataTrimming.Entity)] Type? element)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentException.ThrowIfNullOrEmpty(property);

        DeclaredCollections.GetOrAdd(entity, static _ => []).Add(new ValueCollection(property, field, element));
    }

    /// <summary>
    ///     Records the field behind a collection of children that EF Core's convention cannot pair with the
    ///     navigation by name — <c>_items</c> behind <c>Lines</c> — on the aggregate or on one of its bases. Called by
    ///     generated code.
    /// </summary>
    /// <param name="aggregate">The entity type holding the children.</param>
    /// <param name="navigation">The navigation's name — <c>Lines</c>.</param>
    /// <param name="field">The field it hands out — <c>_items</c>.</param>
    /// <remarks>
    ///     Without it a navigation that hands out a read-only view (<c>_items.AsReadOnly()</c>) has nothing EF Core
    ///     can add a loaded row to, and the first load of the aggregate fails.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void DeclareChildField(Type aggregate, string navigation, string field)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ArgumentException.ThrowIfNullOrEmpty(navigation);
        ArgumentException.ThrowIfNullOrEmpty(field);

        DeclaredChildFields.GetOrAdd(aggregate, static _ => []).Add(new ChildField(navigation, field));
    }

    /// <summary>Whether <paramref name="entity" /> named <paramref name="trait" />. Nothing is named unless it was.</summary>
    /// <param name="entity">The entity type.</param>
    /// <param name="trait">One trait.</param>
    internal static bool Has(Type entity, TraitSet trait) =>
        DeclaredTraits.TryGetValue(entity, out var traits) && (traits & trait) == trait;

    /// <summary>Whether <paramref name="entity" /> is partitioned by tenant, on its own base or through its root.</summary>
    /// <param name="entity">The entity type.</param>
    internal static bool IsPerTenant(Type entity) => Has(entity, TraitSet.PerTenant);

    /// <summary>Whether <paramref name="entity" /> gets a generated delete — <see cref="Deletable" /> or <see cref="SoftDelete" />.</summary>
    /// <param name="entity">The entity type.</param>
    internal static bool IsDeletable(Type entity) =>
        Has(entity, TraitSet.Deletable) || Has(entity, TraitSet.SoftDelete);

    /// <summary>Whether any entity at all is partitioned by tenant.</summary>
    internal static bool AnyTenantScoped => DeclaredTraits.Values.Any(static t => (t & TraitSet.PerTenant) != TraitSet.None);

    /// <summary>The key type <c>PerTenant&lt;TKey&gt;</c> named, or null for plain <see cref="PerTenant" />.</summary>
    /// <param name="entity">The entity type.</param>
    internal static Type? TenantKeyFor(Type entity) => DeclaredTenantKeys.GetValueOrDefault(entity);

    /// <summary>The collections of values <paramref name="entity" /> declared, or none.</summary>
    /// <param name="entity">The entity type.</param>
    internal static IReadOnlyList<ValueCollection> CollectionsFor(Type entity) =>
        DeclaredCollections.TryGetValue(entity, out var collections) ? collections : [];

    /// <summary>The fields behind <paramref name="entity" />'s children that EF Core cannot find by name, or none.</summary>
    /// <param name="entity">The entity type.</param>
    internal static IReadOnlyList<ChildField> ChildFieldsFor(Type entity) =>
        DeclaredChildFields.TryGetValue(entity, out var fields) ? fields : [];
}
