using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Rask.Generators.Shared;

/// <summary>Everything the generated model of one entity is made of.</summary>
internal sealed class ModelShape(
    INamedTypeSymbol entity,
    ITypeSymbol? idType,
    IPropertySymbol? key,
    IReadOnlyList<ModelMember> members,
    IReadOnlyList<ModelValueObject> valueObjects,
    IReadOnlyList<ModelChild> children,
    IReadOnlyList<ModelValueCollection> valueCollections)
{
    /// <summary>The entity.</summary>
    public INamedTypeSymbol Entity { get; } = entity;

    /// <summary>The <c>TId</c> of <c>Entity&lt;TId&gt;</c>.</summary>
    public ITypeSymbol? IdType { get; } = idType;

    /// <summary>
    ///     <c>Entity&lt;TId&gt;.Id</c>, which the model deliberately does NOT carry.
    ///     non-generic base.
    /// </summary>
    /// <remarks>
    ///     A model is what a form posts back, so a key on it is a key the client chooses: an edit could be
    ///     re-pointed at any row by changing one field (overposting). The id travels beside the model instead —
    ///     <c>Update(id, model)</c> — and a create never takes one. It is exposed here only for the generated
    ///     create to assign a key EF Core would not generate.
    /// </remarks>
    public IPropertySymbol? Key { get; } = key;

    /// <summary>The model's properties, in the order they are emitted.</summary>
    public IReadOnlyList<ModelMember> Members { get; } = members;

    /// <summary>Every nested value-object model, each once, in the order they are emitted.</summary>
    public IReadOnlyList<ModelValueObject> ValueObjects { get; } = valueObjects;

    /// <summary>The child collections this aggregate holds, in the order they are emitted.</summary>
    public IReadOnlyList<ModelChild> Children { get; } = children;

    /// <summary>The collections of values it holds, in the order they are emitted.</summary>
    public IReadOnlyList<ModelValueCollection> ValueCollections { get; } = valueCollections;

    /// <summary>Whether the model carries a <see cref="ModelMemberRole.Version" />.</summary>
    public bool Versioned => Members.Any(static m => m.Role == ModelMemberRole.Version);
}
