namespace Rask;

public static partial class Ui
{
    /// <summary>
    /// Where a floating part sits along the side of its anchor that it opens on.
    /// </summary>
    /// <remarks>
    /// The partner of <see cref="Ui.Position" />: a menu <see cref="Ui.Position.Bottom" /> of its trigger can
    /// start at the trigger's start edge, centre under it, or finish at its end edge. Start and end follow the
    /// reading direction, so a right-to-left page mirrors without the call site saying so.
    /// </remarks>
    public enum Align
    {
        /// <summary>The component's own default.</summary>
        Default = 0,

        /// <summary>Flush with the anchor's reading-start edge.</summary>
        Start,

        /// <summary>Centred on the anchor.</summary>
        Center,

        /// <summary>Flush with the anchor's reading-end edge.</summary>
        End,
    }
}
