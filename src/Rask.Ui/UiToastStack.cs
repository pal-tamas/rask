namespace Rask;

/// <summary>What a <see cref="UiToastGroup" /> tells the <see cref="UiToast" /> inside it: stack them, and how.</summary>
/// <param name="Position">The group's corner, which wins over the toast's own.</param>
/// <param name="Expanded">Every toast laid out, rather than a deck that opens under the pointer.</param>
/// <param name="Class">The call site's classes for the group.</param>
internal sealed record UiToastStack(Ui.ToastPosition? Position, bool Expanded, string? Class);
