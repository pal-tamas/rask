using Microsoft.CodeAnalysis;
using Rask.Cqrs.Generators;

namespace Rask.Generators.Shared;

/// <summary>Everything the read face of one entity is made of.</summary>
/// <remarks>
/// Strings and equatable arrays only: an incremental generator that carries an <c>ISymbol</c> between steps
/// keeps a whole compilation alive and never hits its cache.
/// </remarks>
/// <param name="Name">The face's simple name — <c>OrderRead</c>.</param>
/// <param name="FullyQualifiedName">The face's fully-qualified name — <c>global::Shop.OrderRead</c>.</param>
/// <param name="Namespace">The namespace it is emitted into, empty for the global one.</param>
/// <param name="SourceName">The write type's simple name — <c>Order</c>.</param>
/// <param name="SourceTypeName">The write type it reads — <c>global::Shop.Order</c>.</param>
/// <param name="Accessibility">The write type's accessibility, which the face takes.</param>
/// <param name="TableName">The table the convention derives, overridden at runtime by the write model.</param>
/// <param name="IdTypeName">The key's fully-qualified type.</param>
/// <param name="IsAggregate">Whether the source is an aggregate root rather than a child.</param>
/// <param name="Members">The primitive columns, in the order they are emitted.</param>
/// <param name="References">The ids that might be references, for the emit pass to resolve.</param>
/// <param name="Children">The child collections.</param>
/// <param name="ValueCollections">The collections of values: tags, stops.</param>
/// <param name="Location">Where the entity is declared, for a diagnostic to point at.</param>
internal sealed record ReadShape(
    string Name,
    string FullyQualifiedName,
    string Namespace,
    string SourceName,
    string SourceTypeName,
    string Accessibility,
    string TableName,
    string IdTypeName,
    bool IsAggregate,
    EquatableArray<ReadMember> Members,
    EquatableArray<ReadReference> References,
    EquatableArray<ReadChildCollection> Children,
    EquatableArray<ReadValueCollection> ValueCollections,
    SymbolLocation? Location);
