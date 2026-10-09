namespace Rask;

public static partial class Ui
{
    /// <summary>
    ///     How <see cref="UiTabs" /> draws its tabs.
    /// </summary>
    public enum TabsVariant
    {
        /// <summary>A row on a hairline, the selected tab underlined. The default.</summary>
        Default,

        /// <summary>Button-like tabs sharing one filled track, the selected one raised out of it.</summary>
        Segmented,

        /// <summary>Separate pills, the selected one filled.</summary>
        Pills,
    }
}
