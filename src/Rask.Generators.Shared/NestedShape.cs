namespace Rask.Generators.Shared;

/// <summary>A named shape reached through some number of arrays or dictionaries.</summary>
/// <param name="Name">The shape's TypeScript name.</param>
/// <param name="Depth">
///     How many containers stand between the property and the shape: 0 for a plain object, 1 for a
///     list or a dictionary of them, 2 for a list of lists.
/// </param>
internal sealed record NestedShape(string Name, int Depth);
