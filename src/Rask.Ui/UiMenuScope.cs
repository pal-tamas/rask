namespace Rask.Ui;

/// <summary>One entry a dropdown's keyboard cursor can land on, in render order.</summary>
/// <param name="Ordinal">Its position among every entry in the dropdown, submenus included.</param>
/// <param name="Parent">The ordinal of the submenu it sits in, or -1 for the top level.</param>
/// <param name="Text">What type-ahead matches against.</param>
/// <param name="Disabled">Skipped by the cursor.</param>
/// <param name="IsSub">A submenu trigger: ArrowRight opens it.</param>
internal readonly record struct UiMenuEntry(int Ordinal, int Parent, string Text, bool Disabled, bool IsSub);

/// <summary>
/// What a <see cref="UiDropdown" /> tells the items inside it, and what they tell it back.
/// </summary>
/// <remarks>
/// <para>
/// The items are written by the CALLER, inside the dropdown's children, so there is no call site at which the
/// dropdown could hand each one its id or its highlighted state — the reason <see cref="UiAccordionState" />
/// exists too. Unlike an accordion, a menu also needs the reverse: the keyboard cursor moves over items the
/// dropdown never constructed, so each item REGISTERS here as it renders, in document order, and the dropdown's
/// key handler walks that list.
/// </para>
/// <para>
/// Rebuilt on every render of the dropdown and filled by that same render walk, so the list the next key press
/// reads is the list on screen. Items opt out of the render cache for the same reason: a cached item would not
/// register, and the cursor would skip it.
/// </para>
/// </remarks>
internal sealed class UiMenuScope(
    string prefix,
    int active,
    IReadOnlySet<int> openSubs,
    bool keepOpen,
    Func<int, Task> toggleSub,
    string? query = null,
    bool options = false)
{
    private readonly List<UiMenuEntry> _entries = [];

    /// <summary>Everything registered so far this render.</summary>
    internal IReadOnlyList<UiMenuEntry> Entries => _entries;

    /// <summary>The ordinal the keyboard cursor is on, or -1.</summary>
    internal int Active => active;

    /// <summary>Whether a dropdown-wide <c>KeepOpen</c> keeps the menu up after a pick.</summary>
    internal bool KeepOpen => keepOpen;

    /// <summary>Opens or closes the submenu at an ordinal — a click or a tap on its trigger.</summary>
    internal Func<int, Task> ToggleSub => toggleSub;

    /// <summary>Whether the submenu at <paramref name="ordinal" /> is open from the keyboard or a tap.</summary>
    internal bool IsOpen(int ordinal) => openSubs.Contains(ordinal);

    /// <summary>The element id of the entry at <paramref name="ordinal" />, which aria-activedescendant names.</summary>
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
    ///     in the reader's culture, as <c>UiSelect.Searchable</c> matches.
    /// </summary>
    internal bool Hides(string text) =>
        Filtering
        && System.Globalization.CultureInfo.CurrentCulture.CompareInfo.IndexOf(
            text,
            query!,
            System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) < 0;

    /// <summary>Takes the next ordinal for an entry rendering now.</summary>
    internal int Register(int parent, string text, bool disabled, bool isSub)
    {
        var ordinal = _entries.Count;
        _entries.Add(new UiMenuEntry(ordinal, parent, text, disabled, isSub));
        return ordinal;
    }
}

/// <summary>Where in a dropdown an item is rendering: the dropdown's scope, and the submenu it sits in.</summary>
/// <remarks>
///     Provided again by each <see cref="UiMenuSub" /> for its own children, so an item needs no idea how deep it is:
///     the nearest provider answers.
/// </remarks>
internal sealed record UiMenuLevel(UiMenuScope Scope, int Parent);
