namespace Rask.Ui;

/// <summary>What an accordion tells its sections. Not a call site's concern.</summary>
/// <param name="Open">The key of the open section.</param>
/// <param name="OnOpen">What to call when a section is asked to open or close.</param>
// Qualified rather than imported: `using System.ComponentModel` puts a SECOND `Component` in scope and
// every `Component` in this file becomes CS0104-ambiguous.
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed record UiAccordionState(string? Open, Action<string?>? OnOpen);
