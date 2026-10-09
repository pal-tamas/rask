namespace Rask;

/// <summary>
/// What a <see cref="UiMenu" /> tells the rows inside it, and what they tell it back.
/// </summary>
/// <remarks>
/// <para>
/// The rows are written by the CALLER, inside the menu's children, so there is no call site at which the menu
/// could hand each one its id — the reason <see cref="UiAccordionScope" /> exists too. Each row REGISTERS here as
/// it renders, in document order; a command palette, whose cursor the page keeps, walks that list from its key
/// handler. A menu's cursor is the runtime's (<c>data-rask-menu-cursor</c>), and it reads the rows off the page.
/// </para>
/// <para>
/// Rebuilt on every render of the menu and filled by that same render walk, so the list the next key press reads
/// is the list on screen. Rows opt out of the render cache for the same reason: a cached row would not register,
/// and the cursor would skip it.
/// </para>
/// </remarks>
internal sealed class UiMenuScope(string prefix, int active = -1, string? query = null, bool options = false)
{
    private readonly List<UiMenuEntry> _entries = [];

    /// <summary>Everything registered so far this render.</summary>
    internal IReadOnlyList<UiMenuEntry> Entries => _entries;

    /// <summary>
    ///     The ordinal the cursor is on, where the page keeps one — a command palette's. A <see cref="UiMenu" />
    ///     keeps none: its cursor is the runtime's, in the browser, and this is -1.
    /// </summary>
    internal int Active => active;

    /// <summary>The element id of the row at <paramref name="ordinal" />.</summary>
    internal string ItemId(int ordinal) => prefix + "-mi-" + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    ///     Whether the entries are a <c>listbox</c>'s <c>option</c>s rather than a menu's items — a command palette,
    ///     where focus stays in the search box and the list is what it controls.
    /// </summary>
    internal bool AsOptions => options;

    /// <summary>Whether a query is narrowing the list, which is when a separator has nothing left to separate.</summary>
    internal bool Filtering => !string.IsNullOrEmpty(query);

    /// <summary>
    ///     Whether an entry with <paramref name="text" /> is filtered out by the query: case- and accent-insensitive,
    ///     in the reader's culture, as <c>Ui.Select.Searchable</c> matches.
    /// </summary>
    internal bool Hides(string text) =>
        Filtering
        && System.Globalization.CultureInfo.CurrentCulture.CompareInfo.IndexOf(
            text,
            query!,
            System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) < 0;

    /// <summary>Takes the next ordinal for a row rendering now.</summary>
    internal int Register(int parent, string text, bool disabled, bool isSub)
    {
        var ordinal = _entries.Count;
        _entries.Add(new UiMenuEntry(ordinal, parent, text, disabled, isSub));
        return ordinal;
    }
}
