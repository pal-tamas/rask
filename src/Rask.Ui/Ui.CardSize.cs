namespace Rask;

public static partial class Ui
{
    /// <summary>How much room a <see cref="UiCard" /> gives: its padding, its corners, the space between its parts. Flux's <c>size</c>.</summary>
    public enum CardSize
    {
        /// <summary>The default, for most content.</summary>
        Md = 0,

        /// <summary>Compact, for dense lists and sidebars.</summary>
        Xs,

        /// <summary>Tight, for small widgets and stat tiles.</summary>
        Sm,

        /// <summary>Roomy, for forms and settings.</summary>
        Lg,
    }
}
