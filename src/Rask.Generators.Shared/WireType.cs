using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Rask.Generators.Shared;

/// <summary>The wire shape of one type, as a tree the emitter walks.</summary>
internal sealed class WireType
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

    /// <summary>
    ///     For <see cref="WireKind.Object" />: the symbol, used to key generated codec methods. Null for a
    ///     shape with no symbol to hold — a generated model another generator emits, which this
    ///     compilation cannot see (see <see cref="GeneratedModelShape" />).
    /// </summary>
    public INamedTypeSymbol? Symbol { get; set; }

    /// <summary>
    ///     For <see cref="WireKind.Object" />: whether the type is a reference type, stated outright for a
    ///     shape that has no <see cref="Symbol" /> to ask. Null means "ask the symbol".
    /// </summary>
    public bool? IsReference { get; set; }

    /// <summary>
    ///     For <see cref="WireKind.Object" />: the simple name a declaration of this shape takes, for a
    ///     shape that has no <see cref="Symbol" /> to take it from.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>Whether a value of this shape can be null, and so needs null handling on both sides.</summary>
    public bool IsReferenceType => IsReference ?? Symbol?.IsReferenceType == true;

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
}
