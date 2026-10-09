namespace Rask;

/// <summary>
///     What a <see cref="UiTabGroup" /> gives the tablist and the panels inside it: ids that tie a tab to its
///     panel, and the one place the panels can read which tab is selected.
/// </summary>
/// <remarks>
///     The selection belongs to the <see cref="UiTabs" />, which is a sibling of the panels rather than their
///     parent, so the tablist hands its scope over as it renders and the panels — later in document order —
///     read it from here.
/// </remarks>
internal sealed class UiTabGroupScope(string prefix, bool findable)
{
    /// <summary>The tablist's own scope, once the tablist has rendered.</summary>
    internal UiTabScope? Tabs { get; set; }

    /// <summary>Whether hidden panels stay reachable by the browser's find-in-page.</summary>
    internal bool Findable => findable;

    /// <summary>The element id of a tab, which its panel's <c>aria-labelledby</c> names.</summary>
    internal string TabId(string name) => prefix + "-tab-" + name;

    /// <summary>The element id of a panel, which its tab's <c>aria-controls</c> names.</summary>
    internal string PanelId(string name) => prefix + "-panel-" + name;
}
