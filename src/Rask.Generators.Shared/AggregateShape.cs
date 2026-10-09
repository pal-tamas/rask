using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Rask.Generators.Shared;

/// <summary>
///     The one definition of what the <c>Rask.Data</c> generators and analyzers treat as an entity, an aggregate and a
///     value object — so the registry that maps them, the generator that writes them and the analyzers that check
///     them never disagree.
/// </summary>
/// <remarks>
///     Symbol-based and stateless: safe to call from any output callback over any compilation.
/// </remarks>
internal static class AggregateShape
{
    /// <summary>How deep value objects nest inside an entity before the walk stops.</summary>
    public const int MaxValueObjectDepth = 4;

    /// <summary>
    ///     The tenant column. The framework's on every entity, whether the entity declares the property or leaves
    ///     it a shadow one: never on a form model, so a post cannot set it, and never on a read face.
    /// </summary>
    public const string TenantColumn = "TenantId";

    private const string RaskDataNamespace = "Rask.Data";
    private const string EntityBase = "Entity";
    private const string AggregateBase = "Aggregate";

    /// <summary>
    ///     Walks the base chain to <c>Rask.Data.Entity&lt;TId&gt;</c>, handing back <c>TId</c>. Returns false for a type
    ///     that is not an entity at all.
    /// </summary>
    public static bool TryGetIdType(ITypeSymbol symbol, out ITypeSymbol? idType)
    {
        idType = null;

        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            // By name and namespace rather than by display string: the display string carries the type-parameter
            // name, which is not ours to depend on.
            if (current is { Name: EntityBase, TypeArguments.Length: 1 } && IsRaskDataType(current))
            {
                idType = current.TypeArguments[0];
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether <paramref name="symbol" /> derives from <c>Rask.Data.Entity&lt;TId&gt;</c> (an aggregate included).</summary>
    public static bool IsEntity(ITypeSymbol symbol) => TryGetIdType(symbol, out _);

    /// <summary>Whether <paramref name="symbol" /> derives from <c>Rask.Data.Aggregate&lt;TId&gt;</c>.</summary>
    public static bool IsAggregate(ITypeSymbol symbol)
    {
        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            if (current is { Name: AggregateBase, TypeArguments.Length: 1 } && IsRaskDataType(current))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     What a <c>public const</c> of one of Rask.Data's choice enums says on <paramref name="symbol" /> —
    ///     <c>Stamps</c>, <c>Checks</c>, <c>Deletes</c>, <c>Scope</c> — or <c>null</c> when the entity does not say.
    /// </summary>
    /// <remarks>
    ///     Read at compile time, because a const is inlined at every use site and the field itself can be trimmed —
    ///     a runtime reflection read would find nothing in a trimmed publish and quietly fall back to the default,
    ///     so an app would carry different columns in debug and in release.
    /// </remarks>
    public static int? ConstOf(INamedTypeSymbol symbol, string name, string enumName)
    {
        foreach (var field in symbol.GetMembers(name).OfType<IFieldSymbol>())
        {
            if (field is { IsConst: true, ConstantValue: int value } &&
                field.Type is INamedTypeSymbol type && IsRaskDataType(type) &&
                string.Equals(type.Name, enumName, StringComparison.Ordinal))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>Whether the table has <c>CreatedAt</c>: it does unless <c>Stamps</c> leaves <c>Created</c> out.</summary>
    public static bool StampsCreated(INamedTypeSymbol entity) => (StampsOf(entity) & 1) != 0;

    /// <summary>Whether the table has <c>UpdatedAt</c>: it does unless <c>Stamps</c> leaves <c>Updated</c> out.</summary>
    public static bool StampsUpdated(INamedTypeSymbol entity) => (StampsOf(entity) & 2) != 0;

    /// <summary>Whether the aggregate has a <c>Version</c>: it does unless it declares <c>Checks = Concurrency.None</c>.</summary>
    public static bool HasVersion(INamedTypeSymbol entity) =>
        IsAggregate(entity) && ConstOf(entity, "Checks", "Concurrency") != 0;

    /// <summary>Whether the aggregate has a <c>DeletedAt</c>: only when it declares <c>Deletes = Deletion.Soft</c>.</summary>
    public static bool SoftDeletes(INamedTypeSymbol entity) =>
        IsAggregate(entity) && ConstOf(entity, "Deletes", "Deletion") == 1;

    // Timestamps.All when the entity does not say.
    private static int StampsOf(INamedTypeSymbol entity) => ConstOf(entity, "Stamps", "Timestamps") ?? 3;

    /// <summary>A type that becomes a table: concrete, non-static, non-generic, and an entity.</summary>
    public static bool IsMappedEntity(INamedTypeSymbol symbol) =>
        symbol is { IsAbstract: false, IsStatic: false, IsGenericType: false } && IsEntity(symbol);

    /// <summary>A type the generated writes are emitted for: a mapped entity that is an aggregate root.</summary>
    public static bool IsWritableAggregate(INamedTypeSymbol symbol) =>
        IsMappedEntity(symbol) && IsAggregate(symbol) && IsNameableFromGeneratedCode(symbol);

    /// <summary>
    ///     Whether a property of <paramref name="type" /> is a value object: a composite that is not an entity, not a
    ///     collection, and not a value EF Core maps as a column on its own.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         No marker is needed. What an aggregate holds is part of it: a record, a struct or a plain class that is
    ///         not itself an entity is stored as columns on the owner's row.
    ///     </para>
    ///     <para>
    ///         Types from the framework and the providers (<c>System.*</c>, <c>Microsoft.*</c>, NetTopologySuite,
    ///         Npgsql) are never value objects: EF Core and its providers map those themselves.
    ///     </para>
    /// </remarks>
    public static bool IsValueObjectType(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named ||
            named.SpecialType != SpecialType.None ||
            named.TypeKind is not (TypeKind.Class or TypeKind.Struct) ||
            named.IsAbstract ||
            named.IsGenericType ||
            named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T ||
            IsEntity(named) ||
            IsCollection(named) ||
            IsFrameworkType(named))
        {
            return false;
        }

        return StoredProperties(named).Any();
    }

    /// <summary>
    ///     The properties of a value object that hold stored state: public getter, instance, and either a setter or a
    ///     backing field — a computed property (<c>Display =&gt; …</c>) is not one.
    /// </summary>
    public static IEnumerable<IPropertySymbol> StoredProperties(INamedTypeSymbol type) =>
        type.GetMembers().OfType<IPropertySymbol>()
            .Where(static p => !p.IsStatic && !p.IsIndexer &&
                               p.DeclaredAccessibility == Accessibility.Public &&
                               p.GetMethod is { DeclaredAccessibility: Accessibility.Public } &&
                               (p.SetMethod is not null || HasBackingField(p)));

    /// <summary>How generated code writes <paramref name="property" />, or null when it cannot.</summary>
    public static WriteKind? WriteKindOf(IPropertySymbol property)
    {
        if (!IsNameableFromGeneratedCode(property.ContainingType))
        {
            return null;
        }

        if (property.SetMethod is { IsInitOnly: false } setter)
        {
            return setter.DeclaredAccessibility == Accessibility.Public ? WriteKind.Public : WriteKind.Setter;
        }

        return HasBackingField(property) ? WriteKind.Field : null;
    }

    /// <summary><c>Entity&lt;TId&gt;.Id</c> as <paramref name="entity" /> inherits it, or null when it is not an entity.</summary>
    public static IPropertySymbol? KeyProperty(INamedTypeSymbol entity)
    {
        for (var current = entity.BaseType; current is not null; current = current.BaseType)
        {
            if (current is { Name: EntityBase, TypeArguments.Length: 1 } && IsRaskDataType(current))
            {
                return current.GetMembers("Id").OfType<IPropertySymbol>().FirstOrDefault();
            }
        }

        return null;
    }

    /// <summary>Whether every type from <paramref name="type" /> outwards can be named from generated code.</summary>
    public static bool IsNameableFromGeneratedCode(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsRaskDataType(INamedTypeSymbol type) =>
        string.Equals(type.ContainingNamespace?.ToDisplayString(), RaskDataNamespace, StringComparison.Ordinal);

    private static bool HasBackingField(IPropertySymbol property)
    {
        var definition = property.OriginalDefinition;
        return definition.ContainingType.GetMembers().OfType<IFieldSymbol>()
            .Any(f => SymbolEqualityComparer.Default.Equals(f.AssociatedSymbol, definition));
    }

    private static bool IsCollection(INamedTypeSymbol type) =>
        type.AllInterfaces.Any(static i => i.SpecialType == SpecialType.System_Collections_IEnumerable);

    private static bool IsFrameworkType(INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace?.ToDisplayString() ?? "";
        return string.Equals(ns, "System", StringComparison.Ordinal) || ns.StartsWith("System.", StringComparison.Ordinal) ||
               string.Equals(ns, "Microsoft", StringComparison.Ordinal) || ns.StartsWith("Microsoft.", StringComparison.Ordinal) ||
               ns.StartsWith("NetTopologySuite", StringComparison.Ordinal) ||
               ns.StartsWith("Npgsql", StringComparison.Ordinal);
    }
}
