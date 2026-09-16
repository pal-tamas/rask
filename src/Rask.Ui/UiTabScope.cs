namespace Rask.Ui;

/// <summary>
/// What a <see cref="UiTabGroup" /> tells the tabs and panels inside it, and what they tell it back.
/// </summary>
/// <remarks>
/// <para>
/// The same arrangement <see cref="UiMenuScope" /> uses, and for the same reason: the tabs are written by the
/// CALLER, inside the group's children, so there is no call site at which the group could hand each one its id,
/// its selected state or the panel it controls. Each tab REGISTERS as it renders, in document order, and the
/// tablist's key handler walks that list.
/// </para>
/// <para>
/// Rebuilt on every render of the group and filled by that same render walk, so the list the next arrow key
/// reads is the list on screen.
/// </para>
/// </remarks>
internal sealed class UiTabScope(string prefix, string? selected, Func<string, Task> select)
{
    private readonly List<string> _names = [];

    /// <summary>Every tab registered so far this render, in document order.</summary>
    internal IReadOnlyList<string> Names => _names;

    /// <summary>
    ///     Which tab is shown. Unset, it is the FIRST tab to register — a group that opens with nothing shown is
    ///     a set of panels with no way in, and a page should not have to repeat its own first tab's name to avoid
    ///     that.
    /// </summary>
    internal string? Selected => selected ?? (_names.Count != 0 ? _names[0] : null);

    /// <summary>Shows a tab: the group's own callback, which is what writes the page's state.</summary>
    internal Func<string, Task> Select => select;

    /// <summary>The element id of a tab, which its panel's <c>aria-labelledby</c> names.</summary>
    internal string TabId(string name) => prefix + "-tab-" + name;

    /// <summary>The element id of a panel, which its tab's <c>aria-controls</c> names.</summary>
    internal string PanelId(string name) => prefix + "-panel-" + name;

    /// <summary>Takes a place in document order for a tab rendering now.</summary>
    internal void Register(string name)
    {
        if (!_names.Contains(name))
        {
            _names.Add(name);
        }
    }
}
