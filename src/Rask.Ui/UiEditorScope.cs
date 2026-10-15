namespace Rask;

/// <summary>
///     What an editor tells the parts composed inside it: whether it is locked, the HTML it starts with, its
///     placeholder, the id of the editable surface its toolbar controls, and the composer it is the box of.
/// </summary>
internal sealed record UiEditorScope(bool Disabled, string? Value, string? Placeholder, string InputId, UiEditorHost? Host = null);
