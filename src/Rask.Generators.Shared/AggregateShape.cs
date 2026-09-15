using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Rask.Generators.Shared;

/// <summary>How generated code gets a value into a property it cannot assign directly.</summary>
internal enum WriteKind
{
    /// <summary>A public setter, assigned directly.</summary>
    Public,

    /// <summary>A non-public setter, reached through an <c>[UnsafeAccessor]</c> method.</summary>
    Setter,

    /// <summary>No usable setter but a compiler backing field, reached through an <c>[UnsafeAccessor]</c> field.</summary>
    Field,
}

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
        type.ContainingNamespace?.ToDisplayString() == RaskDataNamespace;

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
        return ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal) ||
               ns == "Microsoft" || ns.StartsWith("Microsoft.", StringComparison.Ordinal) ||
               ns.StartsWith("NetTopologySuite", StringComparison.Ordinal) ||
               ns.StartsWith("Npgsql", StringComparison.Ordinal);
    }
}
