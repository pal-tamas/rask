using Microsoft.CodeAnalysis;

namespace Rask.Generators.Shared;

/// <summary>A collection of values an entity holds: the tags of a note, the stops of a trip.</summary>
/// <remarks>
/// One column either way — a primitive collection for plain values, a JSON column for value objects — and
/// replaced wholesale on save, because a value has no identity to reconcile against.
/// </remarks>
internal sealed class ModelValueCollection(
    IPropertySymbol property,
    ITypeSymbol element,
    INamedTypeSymbol? valueObject,
    IFieldSymbol? field,
    ModelValueObject? elementModel = null)
{
    /// <summary>The entity's property: <c>Tags</c>.</summary>
    public IPropertySymbol Property { get; } = property;

    /// <summary>What the collection holds: <c>string</c>, <c>Stop</c>.</summary>
    public ITypeSymbol Element { get; } = element;

    /// <summary>The element as a value object, or null when it is a plain value.</summary>
    public INamedTypeSymbol? ValueObject { get; } = valueObject;

    /// <summary>The backing field to write through, or null when the property itself is writable.</summary>
    public IFieldSymbol? Field { get; } = field;

    /// <summary>
    ///     The nested model each element is carried as on the form — <c>StopModel</c> — or null for a plain
    ///     value, which is carried as itself.
    /// </summary>
    public ModelValueObject? ElementModel { get; } = elementModel;

    /// <summary>The property's name, which is also the model's and the read face's.</summary>
    public string Name => Property.Name;
}
