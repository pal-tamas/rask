using System.Diagnostics.CodeAnalysis;

namespace Rask.Data;

/// <summary>One collection of values an entity holds, as the generator read it.</summary>
/// <param name="Property">The property's name.</param>
/// <param name="Field">The backing field to write through, or null for a writable property.</param>
/// <param name="Element">The value object's type for a JSON collection; null for a primitive one.</param>
internal readonly record struct ValueCollection(
    string Property,
    string? Field,
    [DynamicallyAccessedMembers(DataTrimming.Entity)][property: DynamicallyAccessedMembers(DataTrimming.Entity)] Type? Element);
