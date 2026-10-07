namespace Rask;

/// <summary>
///     What a <see cref="UiModal" /> tells the <see cref="UiModalClose" /> inside it: the dialog to name in a
///     close command, or — when the page owns the state — the callback that closes it.
/// </summary>
internal sealed record UiModalScope(string? Name, Callback Close);
