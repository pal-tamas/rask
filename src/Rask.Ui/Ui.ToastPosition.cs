namespace Rask;

public static partial class Ui
{
    /// <summary>
    ///     Which corner of the viewport toasts appear in: Flux's <c>position</c> on <c>flux:toast</c> and
    ///     <c>flux:toast.group</c>.
    /// </summary>
    /// <remarks>
    ///     An edge and a place along it. Start and end follow the reading direction, so a right-to-left page
    ///     mirrors without the call site saying so.
    /// </remarks>
    public enum ToastPosition
    {
        /// <summary>The bottom edge, at its reading end. The default.</summary>
        BottomEnd,

        /// <summary>The bottom edge, centred.</summary>
        BottomCenter,

        /// <summary>The bottom edge, at its reading start.</summary>
        BottomStart,

        /// <summary>The top edge, at its reading end.</summary>
        TopEnd,

        /// <summary>The top edge, centred.</summary>
        TopCenter,

        /// <summary>The top edge, at its reading start.</summary>
        TopStart,
    }
}
