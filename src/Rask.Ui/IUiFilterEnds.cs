namespace Rask;

/// <summary>What a <see cref="UiFilter" /> opened with <c>Ui.Filter.Range(…)</c> holds: the two ends, of whatever number.</summary>
internal interface IUiFilterEnds
{
    /// <summary>What the filter is called with no label of the page's: its first end's name.</summary>
    string Name { get; }

    /// <summary>The badge the range says, kept under both ends' keys.</summary>
    /// <param name="label">What it filters by.</param>
    UiFilterEntry Filter(string label);

    /// <summary>The two fields, side by side under the label, each bound to the address through <paramref name="set" />.</summary>
    /// <param name="label">What it filters by.</param>
    /// <param name="set">The set it is drawn in; none outside one, where the fields are bound to the page's members.</param>
    Component Draw(string label, UiFilters? set);
}
