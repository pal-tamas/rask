namespace Rask;

/// <summary>
///     <c>.Shown(names)</c> on a <see cref="UiFilter" /> that holds several answers — a select of many vehicles:
///     <c>Ui.Filter.Label("Vehicles").Shown(Vehicles).OnRemove(ClearVehicles)[…]</c>.
/// </summary>
public static class UiFilterShown
{
    /// <summary>
    ///     What the filter holds, as names: none is not in force, one is said by its name ("Vehicles: ABC-123"), and two
    ///     or more by their count ("Vehicles: 40 selected", <see cref="RaskString.FiltersSelectedCount" />) with every
    ///     name in a tooltip on the badge.
    /// </summary>
    /// <param name="filter">The filter.</param>
    /// <param name="names">The names it holds, in the order the tooltip lists them.</param>
    /// <returns>The filter.</returns>
    public static UiFilter Shown(this UiFilter filter, IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(names);
        return filter.Holding(names);
    }
}
