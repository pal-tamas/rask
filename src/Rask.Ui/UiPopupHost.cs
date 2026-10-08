namespace Rask;

/// <summary>
///     What a <see cref="UiDropdown" /> or a <see cref="UiContext" /> tells the panel inside it: the id it is opened
///     by, where it sits, and who to tell when the reader opens or closes it.
/// </summary>
/// <param name="PanelId">The id the trigger's <c>popovertarget</c>, or the right-click hook, names.</param>
/// <param name="Style">Where the panel sits: anchor positioning against the trigger, or the pointer's position.</param>
/// <param name="Controlled">The open state the page asked for, when the page owns it.</param>
/// <param name="Detail">A value for the panel's <c>data-detail</c>.</param>
/// <param name="Toggled">Tells the host the reader opened or closed the panel.</param>
internal sealed record UiPopupHost(
    string PanelId,
    string Style,
    bool? Controlled,
    string? Detail,
    Func<bool, Task> Toggled);
