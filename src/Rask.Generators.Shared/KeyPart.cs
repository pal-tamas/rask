using Microsoft.CodeAnalysis;

namespace Rask.Generators.Shared;

/// <summary>One column of a key of several: <c>DeliveryNoteId</c> of <c>Entity&lt;(DeliveryNote, Transaction)&gt;</c>.</summary>
/// <param name="Name">
///     The property and column — <c>{Aggregate}Id</c>, <c>{Entity}{Column}</c> for a column of the holding entity's
///     key, or the name a scalar part was given.
/// </param>
/// <param name="Type">The column's type: the key type of what the part names, or the scalar's own.</param>
/// <param name="Aggregate">
///     The aggregate the part names — or, for a grandchild, the entity holding it — whose columns Rask generates;
///     null for a scalar the entity declares itself.
/// </param>
internal sealed record KeyPart(string Name, ITypeSymbol Type, INamedTypeSymbol? Aggregate);
