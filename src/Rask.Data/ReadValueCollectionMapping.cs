using System.Diagnostics.CodeAnalysis;

namespace Rask.Data;

/// <summary>A collection of values on a read face: one column, mapped as JSON or as a primitive collection.</summary>
/// <param name="member">The member's name — <c>Tags</c>, <c>Stops</c>.</param>
/// <param name="valueObject">
///     The value object the collection holds, which makes the column JSON; null for plain values.
/// </param>
public sealed class ReadValueCollectionMapping(
    string member,
    [DynamicallyAccessedMembers(DataTrimming.Entity)] Type? valueObject)
{
    /// <summary>The member's name.</summary>
    public string Member { get; } = member;

    /// <summary>The value object held, or null for a primitive collection.</summary>
    [DynamicallyAccessedMembers(DataTrimming.Entity)]
    public Type? ValueObject { get; } = valueObject;
}
