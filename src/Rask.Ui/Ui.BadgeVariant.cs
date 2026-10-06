namespace Rask;

public static partial class Ui
{
    /// <summary>How a <see cref="UiBadge" /> is filled.</summary>
    public enum BadgeVariant
    {
        /// <summary>A tint of its colour under darker text of the same hue. The default.</summary>
        Soft,

        /// <summary>The colour itself under white text, for the status that must not be missed.</summary>
        Solid,
    }
}
