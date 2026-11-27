namespace Rask;

/// <summary>
///     The surface an item list is drawn on, measured on fluxui.dev/components/item: a <see cref="UiItemGroup" />'s, or
///     a <see cref="UiItem" />'s own when it stands alone.
/// </summary>
internal static class UiItemSurface
{
    /// <summary>White with a zinc-900 border at 10% and 12px corners, a faint shadow; in dark white at 10% and no shadow.</summary>
    internal const string Classes =
        "[:where(&)]:bg-white dark:bg-white/10 border border-zinc-900/10 dark:border-white/10 [:where(&)]:rounded-xl shadow-xs dark:shadow-none";
}
