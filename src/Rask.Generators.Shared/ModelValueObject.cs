using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Rask.Generators.Shared;

/// <summary>The nested model a value object becomes: <c>ProductModel.MoneyModel</c> for <c>Money</c>.</summary>
/// <remarks>
/// However the value object itself is built, its nested model is always a generated mutable class — so the wire
/// shape and the TypeScript read only <see cref="ModelName" /> and <see cref="Members" />, never the build.
/// </remarks>
internal sealed class ModelValueObject(
    INamedTypeSymbol type,
    string modelName,
    ModelValueObjectBuild build,
    IMethodSymbol constructor,
    IReadOnlyList<IPropertySymbol>? constructorOrder,
    IReadOnlyList<ModelValueObjectMember> members)
{
    /// <summary>The value object's own type.</summary>
    public INamedTypeSymbol Type { get; } = type;

    /// <summary>The nested class's simple name, unique within its model.</summary>
    public string ModelName { get; } = modelName;

    /// <summary>How the value object is rebuilt from the model.</summary>
    public ModelValueObjectBuild Build { get; } = build;

    /// <summary>The constructor it is built through: the one naming every property, or the parameterless one.</summary>
    public IMethodSymbol Constructor { get; } = constructor;

    /// <summary>The properties in constructor-parameter order, or null when it is rebuilt by object initializer.</summary>
    public IReadOnlyList<IPropertySymbol>? ConstructorOrder { get; } = constructorOrder;

    /// <summary>Whether the value object is rebuilt through a constructor naming every property.</summary>
    public bool ByConstructor => ConstructorOrder is not null;

    /// <summary>Its properties, in declaration order.</summary>
    public IReadOnlyList<ModelValueObjectMember> Members { get; } = members;

    /// <summary>
    ///     Whether it holds exactly one plain value (<c>record Email(string Value)</c>). Such a value object is one column
    ///     and is carried on the model as that value itself — <c>string? Email</c> — with no nested model.
    /// </summary>
    public bool SingleValue => Members.Count == 1 && Members[0].ValueObject is null;
}
