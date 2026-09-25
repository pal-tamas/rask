using System.ComponentModel;

namespace Rask.Core.Diagnostics.DevTools;

/// <summary>One property, as the devtools show it.</summary>
/// <param name="Name">The property's name.</param>
/// <param name="Type">Its declared type.</param>
/// <param name="Value">Its formatted value, or null.</param>
/// <param name="IsRedacted">Whether the value was withheld rather than read.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly record struct DescribedProp(string Name, string Type, string? Value, bool IsRedacted);
