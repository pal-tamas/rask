namespace Rask;

public static partial class Ui
{
    /// <summary>
    ///     The sides on which a <see cref="UiBadge" /> gives its padding back as a negative margin, so it sits
    ///     in a line of text without making the line taller. They combine: <c>Top | Bottom</c>.
    /// </summary>
    [Flags]
    public enum BadgeInset
    {
        /// <summary>No side. The default.</summary>
        None = 0,

        /// <summary>The top edge.</summary>
        Top = 1,

        /// <summary>The bottom edge.</summary>
        Bottom = 2,

        /// <summary>The edge the line starts at.</summary>
        Left = 4,

        /// <summary>The edge the line ends at.</summary>
        Right = 8,
    }
}
