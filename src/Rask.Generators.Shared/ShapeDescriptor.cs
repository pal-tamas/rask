using System.Collections.Generic;

namespace Rask.Generators.Shared;

/// <summary>Which properties of one shape carry dates, and which lead to another shape.</summary>
internal sealed class ShapeDescriptor(IReadOnlyList<string> instants, IReadOnlyDictionary<string, NestedShape> nested)
{
    /// <summary>Properties that are instants, and so become <c>Date</c>.</summary>
    public IReadOnlyList<string> Instants { get; } = instants;

    /// <summary>Properties whose value is another named shape, by that shape's TypeScript name.</summary>
    public IReadOnlyDictionary<string, NestedShape> Nested { get; } = nested;

    /// <summary>Whether this shape, as written, needs the runtime to touch it at all.</summary>
    public bool IsEmpty => Instants.Count == 0 && Nested.Count == 0;
}
