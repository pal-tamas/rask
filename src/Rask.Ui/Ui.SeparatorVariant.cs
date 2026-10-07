namespace Rask;

public static partial class Ui
{
    /// <summary>How strongly a <see cref="UiSeparator" /> draws its line — Flux UI's <c>variant</c>.</summary>
    public enum SeparatorVariant
    {
        /// <summary>The standard line.</summary>
        Default = 0,

        /// <summary>A fainter line that blends into the background, for a toolbar or a menu.</summary>
        Subtle,
    }
}
