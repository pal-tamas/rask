using Microsoft.CodeAnalysis;

namespace Rask.Generators.Shared;

/// <summary>A collection of children an aggregate holds: the lines of an order, the items of a basket.</summary>
/// <remarks>
/// Only <c>Entity&lt;TId&gt;</c> children are here. A collection of <c>Aggregate&lt;TId&gt;</c> is somebody else's
/// data — RASK087 says so — and is never carried on the model, never synced, and never deleted on the parent's
/// behalf.
/// </remarks>
internal sealed class ModelChild(
    IPropertySymbol property,
    INamedTypeSymbol childType,
    ModelChildAccess access,
    IFieldSymbol? field)
{
    /// <summary>The aggregate's property: <c>Lines</c>.</summary>
    public IPropertySymbol Property { get; } = property;

    /// <summary>The child entity type: <c>OrderLine</c>.</summary>
    public INamedTypeSymbol ChildType { get; } = childType;

    /// <summary>How generated code writes the collection.</summary>
    public ModelChildAccess Access { get; } = access;

    /// <summary>The backing field, for <see cref="ModelChildAccess.Field" />.</summary>
    public IFieldSymbol? Field { get; } = field;

    /// <summary>The property's name, which is also the model's.</summary>
    public string Name => Property.Name;
}
