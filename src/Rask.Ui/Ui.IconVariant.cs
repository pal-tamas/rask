namespace Rask;

public static partial class Ui
{
    /// <summary>
    ///     Which drawing of an icon <see cref="UiIcon" /> uses. Each is drawn for one size, so the variant sets it.
    /// </summary>
    public enum IconVariant
    {
        /// <summary>24px, stroked at 1.5px. The default.</summary>
        Outline,

        /// <summary>24px, filled.</summary>
        Solid,

        /// <summary>20px, filled.</summary>
        Mini,

        /// <summary>16px, filled.</summary>
        Micro,
    }
}
