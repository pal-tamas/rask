namespace Rask.Generators.Shared;

/// <summary>A collection of values on a read face: one column, queryable like any other.</summary>
/// <remarks>
/// A value object is NOT flattened here, the way a single one is. Flattening is what turns <c>Money Total</c>
/// into two columns; a collection of them is a single JSON column, so there is nothing to flatten it into. The
/// face carries the value object's own type, which is safe for exactly the reason it is a value object: no
/// identity, no behaviour, nothing to save through.
/// </remarks>
/// <param name="Name">The collection's name — <c>Tags</c>, <c>Stops</c>.</param>
/// <param name="ElementTypeName">What it holds — <c>string</c>, <c>global::Trips.Stop</c>.</param>
/// <param name="IsValueObject">Whether the element is a value object, which makes the column JSON.</param>
internal sealed record ReadValueCollection(
    string Name,
    string ElementTypeName,
    bool IsValueObject);
