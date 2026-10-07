namespace Rask;

/// <summary>Everything about how toasts are drawn that is not the toast: the layout's side of it.</summary>
/// <param name="Position">Which corner.</param>
/// <param name="Invert">Dark in light mode and light in dark.</param>
/// <param name="Class">The call site's classes, for each toast.</param>
/// <param name="Duration">How long a toast shows unless it says otherwise.</param>
/// <param name="Stack">The group around it, or <c>null</c> for one toast at a time.</param>
internal readonly record struct UiToastLook(
    Ui.ToastPosition Position, bool Invert, string? Class, TimeSpan Duration, UiToastStack? Stack);
