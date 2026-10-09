namespace Rask;

public static partial class Ui
{
    /// <summary>
    ///     Whether a scrollable <see cref="UiTabs" /> shows its scrollbar.
    /// </summary>
    public enum TabsScrollbar
    {
        /// <summary>The browser's own scrollbar. The default.</summary>
        Show,

        /// <summary>
        ///     No scrollbar on any device — including a desktop, where a reader may have nothing else to
        ///     scroll sideways with.
        /// </summary>
        Hide,
    }
}
