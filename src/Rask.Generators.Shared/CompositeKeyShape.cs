using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Rask.Generators.Shared;

/// <summary>
///     A key of several columns, declared as a tuple of TYPES — <c>Entity&lt;(DeliveryNote, Transaction)&gt;</c> —
///     read the one way every generator needs it.
/// </summary>
/// <remarks>
///     An aggregate part becomes a generated <c>{Aggregate}Id</c> of that aggregate's key type; a named scalar part
///     — <c>(Order, int LineNo)</c> — is a property the entity declares itself. Generators cannot see each other's
///     output, so the read face and the model registry rebuild the parts from here rather than from the properties.
/// </remarks>
internal static class CompositeKeyShape
{
    /// <summary>Whether <paramref name="idType" /> is a key of several columns.</summary>
    public static bool IsComposite(ITypeSymbol? idType) => idType is INamedTypeSymbol { IsTupleType: true };

    /// <summary>Whether <paramref name="entity" /> is keyed by several columns.</summary>
    public static bool HasCompositeKey(INamedTypeSymbol entity) =>
        AggregateShape.TryGetIdType(entity, out var idType) && IsComposite(idType);

    /// <summary>
    ///     The parts of <paramref name="entity" />'s key in tuple order, or empty when its key is one column. Each
    ///     reason the key cannot be built is added to <paramref name="problems" />.
    /// </summary>
    public static IReadOnlyList<KeyPart> PartsOf(INamedTypeSymbol entity, List<string> problems)
    {
        if (!AggregateShape.TryGetIdType(entity, out var idType) || idType is not INamedTypeSymbol { IsTupleType: true } tuple)
        {
            return [];
        }

        var parts = new List<KeyPart>();

        foreach (var element in tuple.TupleElements)
        {
            if (PartOf(element, problems) is { } part)
            {
                parts.Add(part);
            }
        }

        foreach (var twice in parts.GroupBy(static p => p.Name, StringComparer.Ordinal).Where(static g => g.Count() > 1))
        {
            problems.Add($"two parts of its key are both named '{twice.Key}'");
        }

        return parts;
    }

    /// <summary>The parts of <paramref name="entity" />'s key, or empty when it has one column or cannot be built.</summary>
    public static IReadOnlyList<KeyPart> ValidPartsOf(INamedTypeSymbol entity)
    {
        var problems = new List<string>();
        var parts = PartsOf(entity, problems);
        return problems.Count == 0 ? parts : [];
    }

    private static KeyPart? PartOf(IFieldSymbol element, List<string> problems)
    {
        var type = element.Type;

        if (type is INamedTypeSymbol aggregate && AggregateShape.IsAggregate(aggregate))
        {
            if (element.IsExplicitlyNamedTupleElement)
            {
                problems.Add(
                    $"its key part '{aggregate.Name} {element.Name}' names an aggregate, whose column is always "
                    + $"'{aggregate.Name}Id'; drop the name '{element.Name}'");
                return null;
            }

            if (!AggregateShape.TryGetIdType(aggregate, out var keyType) || keyType is null || IsComposite(keyType))
            {
                problems.Add($"the aggregate '{aggregate.Name}' its key names has no key of one column to point at");
                return null;
            }

            return new KeyPart(aggregate.Name + "Id", keyType, aggregate);
        }

        if (AggregateShape.IsEntity(type))
        {
            problems.Add(
                $"its key part '{type.Name}' is an entity that is not an aggregate; a key part is an aggregate, or a "
                + "named value such as 'int LineNo'");
            return null;
        }

        if (!element.IsExplicitlyNamedTupleElement)
        {
            problems.Add(
                $"its key part '{type.ToDisplayString()}' has no name; name it — '{type.ToDisplayString()} LineNo' — "
                + "and declare that property");
            return null;
        }

        return new KeyPart(element.Name, type, null);
    }

    /// <summary>
    ///     Why the generated half of <paramref name="entity" /> cannot be written: everything but the parts
    ///     themselves, which <see cref="PartsOf" /> reports.
    /// </summary>
    public static void AddDeclarationProblems(INamedTypeSymbol entity, IReadOnlyList<KeyPart> parts, List<string> problems)
    {
        if (AggregateShape.IsAggregate(entity))
        {
            problems.Add(
                "it is an aggregate root; a key of several columns is for an entity its aggregate holds — key the "
                + "root by one column");
        }

        if (entity.ContainingType is not null)
        {
            problems.Add("it is nested in another type; declare it at namespace level");
        }

        if (!entity.DeclaringSyntaxReferences.Any(static r =>
                r.GetSyntax() is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax c &&
                c.Modifiers.Any(static m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword))))
        {
            problems.Add("it is not partial, and Rask generates the key's properties and its Id; declare it 'partial'");
        }

        if (OwnMember(entity, "Id") is not null)
        {
            problems.Add("it declares an 'Id' of its own, and the key's Id is generated");
        }

        foreach (var part in parts)
        {
            var own = OwnMember(entity, part.Name);

            if (part.Aggregate is not null)
            {
                if (own is not null)
                {
                    problems.Add($"it declares '{part.Name}', which the key part '{part.Aggregate.Name}' generates; remove it");
                }

                continue;
            }

            if (own is not IPropertySymbol property)
            {
                problems.Add(
                    $"it declares no property '{part.Name}' for the key part '{part.Type.ToDisplayString()} {part.Name}'; "
                    + $"add 'public {part.Type.ToDisplayString()} {part.Name} {{ get; private set; }}'");
                continue;
            }

            if (!SymbolEqualityComparer.Default.Equals(
                    property.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated),
                    part.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)))
            {
                problems.Add(
                    $"its property '{part.Name}' is '{property.Type.ToDisplayString()}', but the key part says "
                    + $"'{part.Type.ToDisplayString()}'");
            }
        }
    }

    // Declared on the entity itself or a base of the app's own — never Rask's Entity<TId>.
    private static ISymbol? OwnMember(INamedTypeSymbol entity, string name)
    {
        for (var type = entity; type is not null && !IsRaskBase(type); type = type.BaseType)
        {
            if (type.GetMembers(name).FirstOrDefault(static m => m is IPropertySymbol or IFieldSymbol or IMethodSymbol) is { } member)
            {
                return member;
            }
        }

        return null;
    }

    private static bool IsRaskBase(INamedTypeSymbol type) =>
        type is { Name: "Entity" or "Aggregate", TypeArguments.Length: 1 } &&
        string.Equals(type.ContainingNamespace?.ToDisplayString(), "Rask.Data", StringComparison.Ordinal);
}
