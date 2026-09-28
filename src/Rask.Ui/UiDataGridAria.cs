namespace Rask;

/// <summary>
///     The ARIA bags a <see cref="UiDataGrid{T,TKey}" /> hands its table and headers: shared and immutable, so a
///     render allocates nothing to say it is busy or how a column is sorted.
/// </summary>
/// <remarks>Outside the generic grid so there is one of each, not one per row type.</remarks>
internal static class UiDataGridAria
{
    internal static readonly IReadOnlyDictionary<string, string?> Busy =
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["busy"] = "true" };

    internal static readonly IReadOnlyDictionary<string, string?> SortNone =
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["sort"] = "none" };

    internal static readonly IReadOnlyDictionary<string, string?> SortAscending =
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["sort"] = "ascending" };

    internal static readonly IReadOnlyDictionary<string, string?> SortDescending =
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["sort"] = "descending" };
}
