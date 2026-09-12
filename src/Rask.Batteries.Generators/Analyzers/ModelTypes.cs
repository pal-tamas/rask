using Microsoft.CodeAnalysis;

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

    private ModelTypes(INamedTypeSymbol? model, INamedTypeSymbol? modelOfId, INamedTypeSymbol? valueObject)
    {
        Model = model;
        ModelOfId = modelOfId;
        ValueObject = valueObject;
    }

    public INamedTypeSymbol? Model { get; }

    public INamedTypeSymbol? ModelOfId { get; }

    public INamedTypeSymbol? ValueObject { get; }

    public static ModelTypes Resolve(Compilation compilation) =>
        new(
            compilation.GetTypeByMetadataName("Rask.Data.Model"),
            compilation.GetTypeByMetadataName("Rask.Data.Model`1"),
            compilation.GetTypeByMetadataName("Rask.Data.IValueObject"));

    /// <summary>
    ///     A class whose base chain reaches <c>Rask.Data.Model</c> — a concrete entity or an abstract base
    ///     between the two. <c>Model</c> and <c>Model&lt;TId&gt;</c> themselves are not entities.
    /// </summary>
    public bool IsEntity(ITypeSymbol type)
    {
        if (Model is null || type is not INamedTypeSymbol { TypeKind: TypeKind.Class } named
                          || SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, Model)
                          || SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, ModelOfId))
        {
            return false;
        }

        for (var current = named.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, Model))
            {
                return true;
            }
        }

        return false;
    }

    public bool IsValueObject(ITypeSymbol type)
    {
        if (ValueObject is null)
        {
            return false;
        }

        foreach (var implemented in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(implemented, ValueObject))
            {
                return true;
            }
        }

        return false;
    }
}
