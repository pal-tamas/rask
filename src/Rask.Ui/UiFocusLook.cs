namespace Rask;

/// <summary>
///     What the keyboard's focus ring needs from a control's own classes (THE KEYBOARD RING in <c>Styles/ui.css</c>): the
///     ring itself is one rule of the kit's sheet; these are the parts that rule cannot know.
/// </summary>
internal static class UiFocusLook
{
    /// <summary>
    ///     A control whose fill IS the accent, focused from the keyboard: a 2px band of the accent's foreground inside the
    ///     accent ring, which would otherwise vanish into the fill. Inset, so nothing around the control clips it.
    /// </summary>
    internal const string OnAccent = "focus-visible:shadow-[inset_0_0_0_4px_var(--color-accent-foreground)]";

    /// <summary>The same band on a label whose hidden input has the focus, while it is chosen and filled with the accent.</summary>
    internal const string OnAccentChecked = "has-checked:has-[:focus-visible]:shadow-[inset_0_0_0_4px_var(--color-accent-foreground)]";

    /// <summary>
    ///     A label drawn round a hidden input: the accent ring, on what the reader sees, while the input has the keyboard's
    ///     focus. Where it lies is the control's: inside a card, pill, button or segment; round a box, radio or switch.
    /// </summary>
    internal const string Ring = "has-[:focus-visible]:[outline:2px_solid_var(--color-accent)]";

    /// <summary>The ring laid inside the control's own box, where no frame around it can clip it.</summary>
    internal const string Inside = "-outline-offset-2";
}
