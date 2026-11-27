using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Rask.Batteries.Generators;

namespace Rask.Generators.Shared;

/// <summary>
///     What a <c>public partial BelongsTo&lt;Vehicle&gt;? Vehicle { get; }</c> declares: the aggregate it names, the
///     foreign key Rask generates beside it (<c>VehicleId</c>, of the vehicle's key type) and whether that key is required.
/// </summary>
/// <remarks>
///     Shared as source because generators cannot see each other's output: the generated <c>VehicleId</c> is nowhere in the
///     symbols the read face, the form model and the analyzers are built from, so each reconstructs it from here.
/// </remarks>
internal static class BelongsToShape
{
    /// <summary>The metadata name of <c>Rask.Data.BelongsTo&lt;TAggregate&gt;</c>.</summary>
    public const string MetadataName = "Rask.Data.BelongsTo`1";

    /// <summary>The suffix of the generated foreign key: <c>Vehicle</c> → <c>VehicleId</c>.</summary>
    public const string KeySuffix = "Id";

    /// <summary>The aggregate <paramref name="type" /> refers to — <c>Vehicle</c> of <c>BelongsTo&lt;Vehicle&gt;?</c> — or null for another type.</summary>
    public static INamedTypeSymbol? TargetOf(ITypeSymbol type) =>
        type is INamedTypeSymbol { Name: "BelongsTo", TypeArguments.Length: 1 } named &&
        string.Equals(named.ContainingNamespace?.ToDisplayString(), "Rask.Data", StringComparison.Ordinal) &&
        named.TypeArguments[0] is INamedTypeSymbol target
            ? target
            : null;

    /// <summary>Whether <paramref name="property" /> is a <c>BelongsTo</c> reference.</summary>
    public static bool Is(IPropertySymbol property) => !property.IsStatic && TargetOf(property.Type) is not null;

    /// <summary>The foreign key <paramref name="property" /> generates: <c>VehicleId</c>.</summary>
    public static string KeyName(IPropertySymbol property) => property.Name + KeySuffix;

    /// <summary>Whether the reference is optional — <c>BelongsTo&lt;Vehicle&gt;?</c> — which makes its key nullable.</summary>
    public static bool IsOptional(IPropertySymbol property) => property.Type.NullableAnnotation == NullableAnnotation.Annotated;

    /// <summary>The key type of the aggregate <paramref name="property" /> names, or null when it names no entity.</summary>
    public static ITypeSymbol? KeyTypeOf(IPropertySymbol property) =>
        TargetOf(property.Type) is { } target && AggregateShape.TryGetIdType(target, out var key) ? key : null;

    /// <summary>
    ///     Every <c>BelongsTo</c> <paramref name="entity" /> declares, its bases' included up to Rask's own, derived first.
    /// </summary>
    public static IEnumerable<IPropertySymbol> Of(INamedTypeSymbol entity)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var type = entity; type is not null && !IsRaskDataType(type); type = type.BaseType)
        {
            foreach (var property in type.GetMembers().OfType<IPropertySymbol>().Where(p => seen.Add(p.Name) && Is(p)))
            {
                yield return property;
            }
        }
    }

    /// <summary>
    ///     Why <paramref name="property" /> cannot be generated, one sentence each — none when it can.
    /// </summary>
    /// <param name="property">The declared reference.</param>
    /// <param name="declaration">Its declaring syntax.</param>
    public static List<string> ProblemsOf(IPropertySymbol property, PropertyDeclarationSyntax declaration)
    {
        var problems = new List<string>();
        var entity = property.ContainingType;
        var target = TargetOf(property.Type)!;

        if (!declaration.Modifiers.Any(SyntaxKind.PartialKeyword) || !IsGetOnly(declaration))
        {
            problems.Add($"it is not declared 'partial {Display(property)} {property.Name} {{ get; }}', which is the half Rask completes");
        }

        if (property.IsStatic)
        {
            problems.Add("it is static, and a reference is a row's");
        }

        for (var type = entity; type is not null; type = type.ContainingType)
        {
            if (!type.DeclaringSyntaxReferences.Any(static r => r.GetSyntax() is TypeDeclarationSyntax t && t.Modifiers.Any(SyntaxKind.PartialKeyword)))
            {
                problems.Add($"'{type.Name}' is not partial, so the foreign key cannot be added to it");
            }
        }

        if (!AggregateShape.IsEntity(entity))
        {
            problems.Add($"'{entity.Name}' is no entity, so it has no row to keep the id in");
        }

        if (!AggregateShape.IsAggregate(target) || !AggregateShape.IsMappedEntity(target))
        {
            problems.Add(AggregateShape.IsEntity(target)
                ? $"'{target.Name}' is a child of another aggregate — refer to its root, or hold the child's id"
                : $"'{target.Name}' is no aggregate");
        }
        else if (target.AllInterfaces.Any(static i => string.Equals(i.Name, "ITenant", StringComparison.Ordinal) && IsRaskDataType(i)))
        {
            problems.Add($"'{target.Name}' is the tenant, which IBelongsTo<{target.Name}> keeps");
        }
        else if (HoldsAsChild(target, entity))
        {
            problems.Add($"'{entity.Name}' is a child of '{target.Name}', which reaches it already");
        }

        if (HasMember(entity, KeyName(property)))
        {
            problems.Add($"'{entity.Name}' already has a '{KeyName(property)}' — remove it: the BelongsTo generates it");
        }

        if (AggregateShape.IsAggregate(entity) &&
            (AggregateShape.TraitsOf(entity) & (AggregateShape.CreateModel | AggregateShape.UpdateModel)) != 0)
        {
            problems.Add($"'{entity.Name}' has a form, which does not carry a BelongsTo yet — write '{KeyName(property)}' through an operation instead");
        }

        return problems;
    }

    /// <summary>The type as the declaration spells it, for a message: <c>BelongsTo&lt;Vehicle&gt;?</c>.</summary>
    public static string Display(IPropertySymbol property) =>
        property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat
            .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier));

    private static bool IsGetOnly(PropertyDeclarationSyntax declaration) =>
        declaration is { ExpressionBody: null, Initializer: null, AccessorList.Accessors.Count: 1 } &&
        declaration.AccessorList.Accessors[0] is { Body: null, ExpressionBody: null } get &&
        get.Keyword.IsKind(SyntaxKind.GetKeyword);

    // A child the aggregate holds in a collection reaches it through that collection: the read face's way back.
    private static bool HoldsAsChild(INamedTypeSymbol target, INamedTypeSymbol entity) =>
        !AggregateShape.IsAggregate(entity) &&
        GeneratedModelShape.Properties(target).Any(p =>
            GeneratedModelShape.DescribeChild(target, p) is { } child &&
            SymbolEqualityComparer.Default.Equals(child.ChildType, entity));

    private static bool HasMember(INamedTypeSymbol entity, string name)
    {
        for (var type = entity; type is not null; type = type.BaseType)
        {
            if (type.GetMembers(name).Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsRaskDataType(INamedTypeSymbol type) =>
        string.Equals(type.ContainingNamespace?.ToDisplayString(), "Rask.Data", StringComparison.Ordinal);
}
