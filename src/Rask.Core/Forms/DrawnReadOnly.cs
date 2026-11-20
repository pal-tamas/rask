namespace Rask.Core.Forms;

// Whether a typed-into form control is drawn read-only: its typed ReadOnly, or a `readonly` written through the
// Attributes escape hatch, which the browser reads just the same.
//
// The browser lets nobody change such a field's value, so a bound one wires no write at all: a frame that writes it
// anyway was written by hand, and finds no handler to run. Its other events (focus, a click, a key) still arrive —
// unlike a disabled control's (DrawnDisabled, #1273), a read-only one is focusable and is not refused as a whole.
// `readonly` means nothing to a checkbox, a radio, a range, a colour, a file or a hidden input, so those are left
// as they are (the caller asks only for a control that is typed into).
internal static class DrawnReadOnly
{
    /// <summary>Whether <paramref name="element" /> is drawn read-only, its own <c>ReadOnly</c> being <paramref name="readOnly" />.</summary>
    internal static bool Is(bool? readOnly, Component element) =>
        readOnly is true || element.ExtraAttrsInternal?.ContainsKey("readonly") == true;
}
