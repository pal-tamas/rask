using System;
using Microsoft.CodeAnalysis;

namespace Rask.Generators.Shared;

/// <summary>One property of a value object's nested model.</summary>
internal sealed class ModelValueObjectMember(IPropertySymbol property, ModelValueObject? valueObject, ModelWriteKind? write)
{
    /// <summary>The value object's property.</summary>
    public IPropertySymbol Property { get; } = property;

    /// <summary>
    ///     For <see cref="ModelValueObjectBuild.AccessorMembers" />: how the property is written after construction.
    ///     Null for every other build.
    /// </summary>
    public ModelWriteKind? Write { get; } = write;

    /// <summary>The nested model for a value object inside a value object, or null for a plain value.</summary>
    public ModelValueObject? ValueObject { get; } = valueObject;

    /// <summary>Whether the property is declared nullable (<c>?</c>).</summary>
    public bool Nullable => Property.Type.NullableAnnotation == NullableAnnotation.Annotated;
}
