namespace Rask.Data;

/// <summary>The field behind a collection of children, as the generator found it.</summary>
/// <param name="Navigation">The navigation's name.</param>
/// <param name="Field">The field it hands out.</param>
internal readonly record struct ChildField(string Navigation, string Field);
