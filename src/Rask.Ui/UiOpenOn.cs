namespace Rask.Ui;

/// <summary>
/// What makes a floating part open.
/// </summary>
/// <remarks>
/// Hovering is daisyUI's <c>dropdown-hover</c>, and it is a pointer affordance only: a hover cannot be
/// performed on a touch screen and has no keyboard equivalent, so a menu that opens only on hover is
/// unreachable for part of its audience. Use it for a preview whose contents are reachable another way,
/// and leave a menu of actions on <see cref="Click" />.
/// </remarks>
public enum UiOpenOn
{
    /// <summary>Opens when the trigger is activated, by pointer or by keyboard. The default.</summary>
    Click = 0,

    /// <summary>Opens while the pointer is over it.</summary>
    Hover,
}
