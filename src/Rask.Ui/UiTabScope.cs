using System.Globalization;

namespace Rask;

/// <summary>
///     What a <see cref="UiTabs" /> tells the tabs inside it: how they are drawn, which one is selected, and
///     how to select another.
/// </summary>
/// <remarks>
///     The tabs are written by the CALLER, inside the tablist's children, so there is no call site at which
///     the tablist could hand each one its state. Each tab asks as it renders, in document order, and is
///     told its name and whether it is the selected one.
/// </remarks>
internal sealed class UiTabScope(
    Ui.TabsVariant variant,
    Ui.TabsSize size,
    string? chosen,
    bool flagged,
    Func<string, Task> select)
{
    private int _count;
    private string? _shown;

    internal Ui.TabsVariant Variant => variant;

    internal Ui.TabsSize Size => size;

    /// <summary>Selects a tab by name: the tablist's own handler, which writes the page's state.</summary>
    internal Func<string, Task> Select => select;

    /// <summary>The selected tab's name, as far as the tabs rendered so far say.</summary>
    internal string? Selected => chosen ?? _shown;

    /// <summary>
    ///     Takes a tab's place in the row and answers what it is called and whether it is selected.
    /// </summary>
    /// <param name="name">The tab's own name; a tab without one is known by its place, <c>"0"</c>, <c>"1"</c>…</param>
    /// <param name="selected">The tab's own <c>Selected</c>, which counts until a tab has been chosen.</param>
    /// <param name="first">
    ///     Whether this tab may be the one shown when nothing says which is: a disabled tab may not.
    /// </param>
    internal (string Name, bool Selected) Place(string? name, bool selected, bool first)
    {
        var known = name ?? _count.ToString(CultureInfo.InvariantCulture);
        _count++;

        if (chosen is not null)
        {
            return (known, string.Equals(chosen, known, StringComparison.Ordinal));
        }

        // A tab that says it is selected is. Otherwise the first tab is — unless a later one in this row
        // says so, which is what `flagged` reports before any of them has rendered.
        var shown = _shown is null && (selected || (!flagged && first));
        if (shown)
        {
            _shown = known;
        }

        return (known, shown);
    }
}
