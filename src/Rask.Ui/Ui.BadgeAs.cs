namespace Rask;

public static partial class Ui
{
    /// <summary>The element a <see cref="UiBadge" /> is.</summary>
    public enum BadgeAs
    {
        /// <summary>A <c>&lt;div&gt;</c>: something to read. The default.</summary>
        Div,

        /// <summary>A <c>&lt;button type="button"&gt;</c>: the whole badge is pressed.</summary>
        Button,
    }
}
