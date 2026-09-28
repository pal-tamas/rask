namespace Rask.DevTools.Probe;

/// <summary>A context value a component provides to everything rendered inside it, as the panel shows it.</summary>
/// <param name="Type">The type it is provided as — the key readers match on.</param>
/// <param name="Name">The name that tells two providers of one type apart, when it has one.</param>
/// <param name="Value">The value, formatted like a prop, or null.</param>
/// <param name="IsRedacted">Whether the value was withheld: its name or type says it is a secret.</param>
internal sealed record DevToolsProvidedContext(string Type, string? Name, string? Value, bool IsRedacted);
