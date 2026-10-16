using Microsoft.CodeAnalysis;

namespace Rask.Generators.Shared;

/// <summary>One column of a key of several: <c>DeliveryNoteId</c> of <c>Entity&lt;(DeliveryNote, Transaction)&gt;</c>.</summary>
/// <param name="Name">The property and column — <c>{Aggregate}Id</c>, or the name a scalar part was given.</param>
/// <param name="Type">The column's type: the aggregate's key type, or the scalar's own.</param>
/// <param name="Aggregate">The aggregate the part names, or null for a scalar the entity declares itself.</param>
internal sealed record KeyPart(string Name, ITypeSymbol Type, INamedTypeSymbol? Aggregate);
