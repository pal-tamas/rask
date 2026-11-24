namespace Rask.Generators.Shared;

/// <summary>One property whose value object's columns are named after <paramref name="Pattern" />.</summary>
/// <param name="Property">The entity's property that holds the value object.</param>
/// <param name="Pattern">The column name, with <c>{Member}</c> where each member's name goes.</param>
/// <param name="Json">
///     Whether <c>b.Json(…)</c> keeps the value object whole in one JSON column instead — it then has no columns of its
///     own to name, and <paramref name="Pattern" /> is empty.
/// </param>
internal sealed record ColumnPattern(string Property, string Pattern, bool Json = false);
