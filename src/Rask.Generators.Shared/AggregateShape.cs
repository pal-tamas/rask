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
    private const string RaskValuesNamespace = "Rask.Values";
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

    // Mirrors Rask.Data.TraitSet. A generator targets netstandard2.0 and never loads the runtime assembly, so the
    // values are restated here and pinned against the enum by a test.
    public const int Created = 1;
    public const int Updated = 2;
    public const int Versioned = 4;
    public const int CreateModel = 8;
    public const int UpdateModel = 16;
    public const int Deletable = 32;
    public const int SoftDelete = 64;
    public const int PerTenant = 128;
    public const int Broadcast = 256;

    /// <summary>
    ///     The traits the base of <paramref name="symbol" /> names — <c>Aggregate&lt;Guid, Timestamps, SoftDelete&gt;</c>
    ///     — as <c>Rask.Data.TraitSet</c> bits. None when it names none: a table carries only what it asks for.
    /// </summary>
    /// <remarks>
    ///     Read at compile time and carried out as an int, since an ISymbol must not cross an incremental-generator step.
    /// </remarks>
    public static int TraitsOf(INamedTypeSymbol symbol)
    {
        var traits = 0;

        foreach (var argument in TraitArguments(symbol))
        {
            traits |= TraitOf(argument);
        }

        return traits;
    }

    /// <summary>Whether the base of <paramref name="symbol" /> names <paramref name="trait" />.</summary>
    public static bool Has(INamedTypeSymbol symbol, int trait) => (TraitsOf(symbol) & trait) == trait;

    /// <summary>
    ///     The key type <c>PerTenant&lt;TKey&gt;</c> names, fully qualified — <c>global::System.Int32</c> — or null
    ///     for plain <c>PerTenant</c> or none.
    /// </summary>
    public static string? TenantKeyOf(INamedTypeSymbol symbol) =>
        TraitArguments(symbol)
            .OfType<INamedTypeSymbol>()
            .Where(static t => t is { Name: "PerTenant", TypeArguments.Length: 1 } && IsRaskDataType(t))
            .Select(static t => t.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            .FirstOrDefault();

    /// <summary>Whether the table has <c>CreatedAt</c>: only when its base names <c>Created</c> or <c>Timestamps</c>.</summary>
    public static bool StampsCreated(INamedTypeSymbol entity) => Has(entity, Created);

    /// <summary>Whether the table has <c>UpdatedAt</c>: only when its base names <c>Updated</c> or <c>Timestamps</c>.</summary>
    public static bool StampsUpdated(INamedTypeSymbol entity) => Has(entity, Updated);

    /// <summary>Whether the aggregate has a <c>Version</c>: only when its base names <c>Versioned</c>.</summary>
    public static bool HasVersion(INamedTypeSymbol entity) => IsAggregate(entity) && Has(entity, Versioned);

    /// <summary>Whether the aggregate has a <c>DeletedAt</c>: only when its base names <c>SoftDelete</c>.</summary>
    public static bool SoftDeletes(INamedTypeSymbol entity) => IsAggregate(entity) && Has(entity, SoftDelete);

    /// <summary>Whether the aggregate gets a generated <c>Delete</c>: <c>Deletable</c> or <c>SoftDelete</c>.</summary>
    public static bool IsDeletable(INamedTypeSymbol entity) =>
        IsAggregate(entity) && (TraitsOf(entity) & (Deletable | SoftDelete)) != 0;

    // The type arguments after TId, on every Rask.Data base in the chain.
    private static IEnumerable<ITypeSymbol> TraitArguments(INamedTypeSymbol symbol)
    {
        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            if (current is { Name: EntityBase or AggregateBase, TypeArguments.Length: > 1 } && IsRaskDataType(current))
            {
                foreach (var argument in current.TypeArguments.Skip(1))
                {
                    yield return argument;
                }
            }
        }
    }

    private static int TraitOf(ITypeSymbol argument) =>
        argument is INamedTypeSymbol named && IsRaskDataType(named)
            ? named.Name switch
            {
                "Created" => Created,
                "Updated" => Updated,
                "Timestamps" => Created | Updated,
                "Versioned" => Versioned,
                "Model" => CreateModel | UpdateModel,
                "CreateModel" => CreateModel,
                "UpdateModel" => UpdateModel,
                "Deletable" => Deletable,
                "SoftDelete" => SoftDelete,
                "PerTenant" => PerTenant,
                "Broadcast" => Broadcast,
                _ => 0,
            }
            : 0;

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
    ///     The value object a property of <paramref name="type" /> holds, or null when it holds none: the type
    ///     itself, or the one-value struct inside <c>T?</c> — optional, exactly as a nullable reference is.
    /// </summary>
    /// <remarks>
    ///     Only a struct that holds ONE value is unwrapped: it is a nullable column. A nullable struct of several
    ///     values is left as the plain value it has always been.
    /// </remarks>
    public static INamedTypeSymbol? ValueObjectTypeOf(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments.Length: 1 } optional)
        {
            return optional.TypeArguments[0] is INamedTypeSymbol held && IsValueObjectType(held) && HoldsOneValue(held) ? held : null;
        }

        return type.WithNullableAnnotation(NullableAnnotation.NotAnnotated) is INamedTypeSymbol named && IsValueObjectType(named)
            ? named
            : null;
    }

    /// <summary>Whether a value object stores exactly one value that is not a value object itself.</summary>
    public static bool HoldsOneValue(INamedTypeSymbol valueObject)
    {
        var stored = StoredProperties(valueObject).Take(2).ToList();
        return stored.Count == 1 && ValueObjectTypeOf(stored[0].Type) is null;
    }

    /// <summary>
    ///     The properties of a value object that hold stored state: public getter, instance, and either a setter or a
    ///     backing field — a computed property (<c>Display =&gt; …</c>) is not one.
    /// </summary>
    /// <remarks>
    ///     A value object declared from a base that holds its value (<c>record DriverName(string Value) : Text(Value, MaxLength: 1000)</c>)
    ///     stores the base's <c>Value</c> and nothing else: the rules its base keeps are not state. One declared from
    ///     <c>Whole</c> (an <c>int</c> or a <c>long</c>) or <c>Value</c> stores its own positional members.
    /// </remarks>
    public static IEnumerable<IPropertySymbol> StoredProperties(INamedTypeSymbol type) =>
        DeclaredValueBaseOf(type) is "Text" or "Number" or "Day" or "Moment"
            ? type.BaseType!.GetMembers("Value").OfType<IPropertySymbol>()
            : OwnStoredProperties(type);

    /// <summary>
    ///     The base record a one-value value object is declared from — <c>Text</c>, <c>Number</c>, <c>Whole</c>,
    ///     <c>Day</c> or <c>Moment</c> of Rask.Values, derived from directly, or <c>Value</c> for a record of ONE member
    ///     that is not a value object itself (<c>record Rank(int Value) : Value</c>) — or null for any other type.
    /// </summary>
    public static string? ValueBaseOf(INamedTypeSymbol type)
    {
        var valueBase = DeclaredValueBaseOf(type);
        return valueBase switch
        {
            "Text" or "Number" or "Whole" or "Day" or "Moment" => valueBase,
            "Value" when HoldsOneOwnValue(type) => valueBase,
            _ => null,
        };
    }

    // One positional member, and not a value object itself: `record Rank(int Value) : Value`.
    private static bool HoldsOneOwnValue(INamedTypeSymbol type)
    {
        var stored = OwnStoredProperties(type).Take(2).ToList();
        return stored.Count == 1 && ValueObjectTypeOf(stored[0].Type) is null;
    }

    private static IEnumerable<IPropertySymbol> OwnStoredProperties(INamedTypeSymbol type) =>
        type.GetMembers().OfType<IPropertySymbol>()
            .Where(static p => !p.IsStatic && !p.IsIndexer &&
                               p.DeclaredAccessibility == Accessibility.Public &&
                               p.GetMethod is { DeclaredAccessibility: Accessibility.Public } &&
                               (p.SetMethod is not null || HasBackingField(p)));

    /// <summary>
    ///     The Rask.Values base record a value object checked as it is made derives from directly — <c>Value</c>,
    ///     <c>Text</c>, <c>Number</c>, <c>Whole</c>, <c>Day</c>, <c>Moment</c> or <c>Range</c> — or null for any other type.
    /// </summary>
    public static string? DeclaredValueBaseOf(INamedTypeSymbol type) =>
        type.BaseType is { Name: "Value" or "Text" or "Number" or "Whole" or "Day" or "Moment" or "Range", Arity: 0, ContainingType: null } valueBase &&
        IsRaskValuesType(valueBase)
            ? valueBase.Name
            : null;

    /// <summary>
    ///     Whether a value object declared from <c>Text</c> says an empty text is a value: <c>Text(Value, Required: false)</c>,
    ///     read from its base call as written (a constant <c>false</c>).
    /// </summary>
    public static bool AllowsEmptyText(INamedTypeSymbol type) =>
        DeclaredValueBaseOf(type) is "Text" &&
        type.DeclaringSyntaxReferences
            .Select(static r => r.GetSyntax())
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.RecordDeclarationSyntax>()
            .SelectMany(static d => d.BaseList?.Types.OfType<Microsoft.CodeAnalysis.CSharp.Syntax.PrimaryConstructorBaseTypeSyntax>() ?? [])
            .SelectMany(static call => call.ArgumentList.Arguments)
            .Any(static a => string.Equals(a.NameColon?.Name.Identifier.ValueText, "Required", StringComparison.Ordinal) &&
                             a.Expression.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.FalseLiteralExpression));

    /// <summary>Whether <paramref name="type" /> derives, at any depth, from Rask.Values' <c>Value</c>.</summary>
    public static bool IsValueRecord(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current is { Name: "Value", Arity: 0, ContainingType: null } && IsRaskValuesType(current))
            {
                return true;
            }
        }

        return false;
    }

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

    private static bool IsRaskValuesType(INamedTypeSymbol type) =>
        string.Equals(type.ContainingNamespace?.ToDisplayString(), RaskValuesNamespace, StringComparison.Ordinal);

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
