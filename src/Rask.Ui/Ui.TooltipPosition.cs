namespace Rask;

public static partial class Ui
{
    /// <summary>Which side of its trigger a <see cref="UiTooltip" /> opens on. Flux's <c>position</c>.</summary>
    public enum TooltipPosition
    {
        /// <summary>Above. The default.</summary>
        Top,

        /// <summary>To the right, whatever the reading direction.</summary>
        Right,

        /// <summary>Below.</summary>
        Bottom,

        /// <summary>To the left, whatever the reading direction.</summary>
        Left,
    }
}
