using System;
using System.Collections.Generic;
using System.Linq;

namespace Rask.Generators.Shared;

/// <summary>The wire shape of one type, as a tree the emitter walks.</summary>
/// <remarks>
///     A value: it holds no symbol, and two shapes are equal when everything the emitter reads off them is. That is
///     what lets a generator carry it across an incremental-pipeline boundary — a shape classified again from an
///     unchanged declaration compares equal, and the output step is served from the cache.
/// </remarks>
internal sealed class WireType : IEquatable<WireType>
{
    /// <summary>What kind of encoding this type gets.</summary>
    public WireKind Kind { get; set; }

    /// <summary>The fully qualified type name, ready to emit.</summary>
    public string Fqn { get; set; } = string.Empty;

    /// <summary>For <see cref="WireKind.Scalar" />: the <c>WireJson</c> reader, or a reader expression.</summary>
    public string? ReadExpression { get; set; }

    /// <summary>For <see cref="WireKind.Scalar" />: how to write the value, with <c>{0}</c> for the value.</summary>
    public string? WriteExpression { get; set; }

    /// <summary>The wrapped shape: a nullable's underlying type, a sequence's element, a map's value.</summary>
    public WireType? Inner { get; set; }

    /// <summary>For <see cref="WireKind.Sequence" />: how to rebuild the collection.</summary>
    public SequenceShape Sequence { get; set; }

    /// <summary>For <see cref="WireKind.Object" />: whether the type is a reference type. Null means it is not.</summary>
    public bool? IsReference { get; set; }

    /// <summary>
    ///     For <see cref="WireKind.Object" />: the simple name a declaration of this shape takes, for a generated
    ///     model another generator emits, which this compilation cannot see (see <see cref="GeneratedModelShape" />).
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    ///     For <see cref="WireKind.Object" /> and <see cref="WireKind.OneValue" />: a <c>Rask.Data</c> value object,
    ///     which the reader rebuilds through its generated <c>Create</c> inside a loading scope — unchecked, as a row is
    ///     loaded.
    /// </summary>
    public bool ValueObject { get; set; }

    /// <summary>Whether a value of this shape can be null, and so needs null handling on both sides.</summary>
    public bool IsReferenceType => IsReference == true;

    /// <summary>For <see cref="WireKind.Object" />: the properties, in declaration order.</summary>
    public List<WireMember> Members { get; } = new();

    /// <summary>
    ///     For <see cref="WireKind.Object" />: the constructor parameter names, in order, when the type is
    ///     built by constructor. Null when it is built with an object initializer.
    /// </summary>
    public List<string>? ConstructorParameters { get; set; }

    /// <summary>For <see cref="WireKind.Unsupported" />: what has no encoding, in the diagnostic's words.</summary>
    public string? Reason { get; set; }

    /// <summary>True when this shape, or anything inside it, carries a file.</summary>
    public bool ContainsFile =>
        Kind == WireKind.File
        || (Inner?.ContainsFile ?? false)
        || Members.Any(m => m.Type.ContainsFile);

    /// <inheritdoc />
    public bool Equals(WireType? other) =>
        other is not null
        && (ReferenceEquals(this, other)
            || (Kind == other.Kind
                && string.Equals(Fqn, other.Fqn, StringComparison.Ordinal)
                && string.Equals(ReadExpression, other.ReadExpression, StringComparison.Ordinal)
                && string.Equals(WriteExpression, other.WriteExpression, StringComparison.Ordinal)
                && Equals(Inner, other.Inner)
                && Sequence == other.Sequence
                && IsReference == other.IsReference
                && ValueObject == other.ValueObject
                && string.Equals(Name, other.Name, StringComparison.Ordinal)
                && string.Equals(Reason, other.Reason, StringComparison.Ordinal)
                && Members.SequenceEqual(other.Members)
                && (ConstructorParameters is null
                    ? other.ConstructorParameters is null
                    : other.ConstructorParameters is not null
                      && ConstructorParameters.SequenceEqual(other.ConstructorParameters, StringComparer.Ordinal))));

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as WireType);

    /// <inheritdoc />
    public override int GetHashCode() =>
        unchecked(((int)Kind * 397) ^ StringComparer.Ordinal.GetHashCode(Fqn) ^ (Members.Count * 31));
}
