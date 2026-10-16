namespace Rask;

/// <summary>
/// What a <see cref="UiMenu" /> tells the rows inside it, and what they tell it back.
/// </summary>
/// <remarks>
/// <para>
/// The rows are written by the CALLER, inside the menu's children, so there is no call site at which the menu
/// could hand each one its id — the reason <see cref="UiAccordionScope" /> exists too. Each row REGISTERS here as
/// it renders, in document order, which is what gives it its id. The cursor is the runtime's
/// (<c>data-rask-menu-cursor</c>), and reads the rows off the page.
/// </para>
/// <para>
/// Rebuilt on every render of the menu and filled by that same render walk. Rows opt out of the render cache
/// for that reason: a cached row would not register, and the rows after it would take its id.
/// </para>
/// </remarks>
internal sealed class UiMenuScope(string prefix)
{
    private readonly List<UiMenuEntry> _entries = [];

    /// <summary>Everything registered so far this render.</summary>
    internal IReadOnlyList<UiMenuEntry> Entries => _entries;

    /// <summary>The element id of the row at <paramref name="ordinal" />.</summary>
    internal string ItemId(int ordinal) => prefix + "-mi-" + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Takes the next ordinal for a row rendering now.</summary>
    internal int Register(int parent, string text, bool disabled, bool isSub)
    {
        var ordinal = _entries.Count;
        _entries.Add(new UiMenuEntry(ordinal, parent, text, disabled, isSub));
        return ordinal;
    }
}
