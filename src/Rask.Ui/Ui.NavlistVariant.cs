namespace Rask;

public static partial class Ui
{
    /// <summary>
    ///     How a <see cref="UiNavlist" /> draws its rows.
    /// </summary>
    public enum NavlistVariant
    {
        /// <summary>The current row is tinted.</summary>
        Default,

        /// <summary>The current row is a white, outlined pill — for a navlist on a tinted ground, as in Flux's sidebar.</summary>
        Outline,
    }
}
