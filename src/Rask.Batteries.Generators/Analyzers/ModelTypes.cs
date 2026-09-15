using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Rask.Generators.Shared;

namespace Rask.Data.Generators.Analyzers;

/// <summary>
///     The Rask.Data types the model-state analyzers key on, resolved once per compilation.
/// </summary>
/// <remarks>
///     Resolved by metadata name, so a compilation that does not reference Rask.Data gets no symbol and the
///     analyzers register nothing — the batteries assembly reaches every Rask.Cqrs consumer, most of which
///     have no entities at all.
/// </remarks>
internal sealed class ModelTypes
{
    // A property that COULD have a fix, but where the mechanical edit would not compile. Read by the code
    // fixes in Rask.Generators.CodeFixes, which cannot reference this assembly, so the key is repeated there.
    public const string NoFixProperty = "RaskNoFix";

    private ModelTypes(INamedTypeSymbol? entityOfId, INamedTypeSymbol? aggregateOfId)
    {
        EntityOfId = entityOfId;
        AggregateOfId = aggregateOfId;
    }

    /// <summary><c>Rask.Data.Entity&lt;TId&gt;</c>, or null when the compilation does not reference Rask.Data.</summary>
    public INamedTypeSymbol? EntityOfId { get; }

    /// <summary><c>Rask.Data.Aggregate&lt;TId&gt;</c>.</summary>
    public INamedTypeSymbol? AggregateOfId { get; }

    public static ModelTypes Resolve(Compilation compilation) =>
        new(
            compilation.GetTypeByMetadataName("Rask.Data.Entity`1"),
            compilation.GetTypeByMetadataName("Rask.Data.Aggregate`1"));

    /// <summary>
    ///     A class whose base chain reaches <c>Rask.Data.Entity&lt;TId&gt;</c> — a concrete entity, an aggregate, or an
    ///     abstract base between them. <c>Entity&lt;TId&gt;</c> and <c>Aggregate&lt;TId&gt;</c> themselves are not.
    /// </summary>
    public bool IsEntity(ITypeSymbol type)
    {
        if (EntityOfId is null || type is not INamedTypeSymbol { TypeKind: TypeKind.Class } named
                               || IsFrameworkBase(named))
        {
            return false;
        }

        return AggregateShape.IsEntity(named);
    }

    /// <summary>Whether <paramref name="type" /> is one of Rask's own bases (<c>Entity&lt;TId&gt;</c>, <c>Aggregate&lt;TId&gt;</c>).</summary>
    public bool IsFrameworkBase(ITypeSymbol type) =>
        SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, EntityOfId) ||
        SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, AggregateOfId);

    /// <summary>
    ///     Every value-object type <paramref name="entity" /> holds, however deep, declared in source — the types whose
    ///     state is part of the entity's even though they carry no marker.
    /// </summary>
    public static IEnumerable<INamedTypeSymbol> ValueObjectsOf(INamedTypeSymbol entity)
    {
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var pending = new Stack<(INamedTypeSymbol Owner, int Depth)>();
        pending.Push((entity, 0));

        while (pending.Count > 0)
        {
            var (owner, depth) = pending.Pop();
            if (depth >= AggregateShape.MaxValueObjectDepth)
            {
                continue;
            }

            foreach (var member in owner.GetMembers())
            {
                if (member is not IPropertySymbol { IsStatic: false } property ||
                    property.Type is not INamedTypeSymbol type ||
                    !AggregateShape.IsValueObjectType(type) ||
                    type.DeclaringSyntaxReferences.Length == 0 ||
                    !seen.Add(type))
                {
                    continue;
                }

                yield return type;
                pending.Push((type, depth + 1));
            }
        }
    }
}
