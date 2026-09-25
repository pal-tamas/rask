using System;
using Microsoft.CodeAnalysis;

namespace Rask.Generators.Shared;

/// <summary>One property of an entity that its generated model carries.</summary>
internal sealed class ModelMember(IPropertySymbol property, ModelMemberRole role, ModelWriteKind? write, ModelValueObject? valueObject)
{
    /// <summary>The entity's property; the model's property has the same name.</summary>
    public IPropertySymbol Property { get; } = property;

    /// <summary>The part it plays.</summary>
    public ModelMemberRole Role { get; } = role;

    /// <summary>How it is written back. Null only for <see cref="ModelMemberRole.Version" />, which never is.</summary>
    public ModelWriteKind? Write { get; } = write;

    /// <summary>The nested model standing in for a value object, or null for a plain value.</summary>
    public ModelValueObject? ValueObject { get; } = valueObject;

    /// <summary>Whether the property is declared nullable (<c>?</c>).</summary>
    public bool Nullable => Property.Type.NullableAnnotation == NullableAnnotation.Annotated;
}
