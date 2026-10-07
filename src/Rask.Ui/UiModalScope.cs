namespace Rask;

/// <summary>What a <see cref="UiModal" /> tells the <see cref="UiModalClose" /> inside it: the id of the dialog to close.</summary>
internal sealed record UiModalScope(string Id);
