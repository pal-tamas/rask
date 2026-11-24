namespace Rask.Generators.Shared;

/// <summary>One property whose value object's columns are named after <paramref name="Pattern" />.</summary>
/// <param name="Property">The entity's property that holds the value object.</param>
/// <param name="Pattern">The column name, with <c>{Member}</c> where each member's name goes.</param>
internal sealed record ColumnPattern(string Property, string Pattern);
