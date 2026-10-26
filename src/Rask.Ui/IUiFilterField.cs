namespace Rask;

/// <summary>A control a <see cref="UiFilters" /> can hold: one that says what it filters by, in the words it shows.</summary>
internal interface IUiFilterField
{
    /// <summary>What the set shows for it: its label, what it holds, and whether that is a filter in force.</summary>
    /// <remarks>Reads and changes nothing, so the page may ask for it before the set is drawn.</remarks>
    UiFilterEntry Filter();

    /// <summary>
    ///     Hands the control to the set, as the set is drawn: bound to the address (<see cref="UiFilterCell{T}" />),
    ///     written as it is chosen, and keeping the id its own member gives it.
    /// </summary>
    /// <param name="set">The set it is drawn in, which takes its list back to the first page as it is changed.</param>
    /// <remarks>
    ///     When the set is drawn and not before: the page's own render puts back every prop its chain did not write
    ///     once it returns, so a change made while the page is still rendering would not last.
    /// </remarks>
    void Take(UiFilters set);
}
