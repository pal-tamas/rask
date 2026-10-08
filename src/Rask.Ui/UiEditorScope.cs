namespace Rask;

/// <summary>
///     What an editor tells the parts composed inside it: whether it is locked, the HTML it starts with, its
///     placeholder, and the id of the editable surface its toolbar controls.
/// </summary>
internal sealed record UiEditorScope(bool Disabled, string? Value, string? Placeholder, string InputId);
