namespace Rask;

/// <summary>The ARIA an option of a drawn list carries — <c>Ui.Select</c>'s today — shared, since there are three answers.</summary>
/// <remarks>
///     aria-disabled is OMITTED when the option is enabled, never nulled: a null renders the attribute valueless, and a
///     valueless aria-disabled reads as "true" — so the tidy conditional value would mark every option unavailable.
/// </remarks>
internal static class UiOptionAria
{
    private static readonly IReadOnlyDictionary<string, string?> Unavailable =
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["selected"] = "false", ["disabled"] = "true" };

    private static readonly IReadOnlyDictionary<string, string?> Selected =
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["selected"] = "true" };

    private static readonly IReadOnlyDictionary<string, string?> Unselected =
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["selected"] = "false" };

    internal static IReadOnlyDictionary<string, string?> For(bool disabled, bool selected)
    {
        if (disabled)
        {
            return Unavailable;
        }

        return selected ? Selected : Unselected;
    }

    /// <summary>The same for a row of a windowed list, which says how many rows there are and which it is: the rest are not on the page to count.</summary>
    /// <param name="disabled">Whether the row is out of reach.</param>
    /// <param name="selected">Whether the row is picked.</param>
    /// <param name="position">The row's place among the rows on offer, from 1.</param>
    /// <param name="count">How many rows are on offer.</param>
    internal static IReadOnlyDictionary<string, string?> For(bool disabled, bool selected, int position, int count) =>
        new Dictionary<string, string?>(For(disabled, selected), StringComparer.Ordinal)
        {
            ["setsize"] = count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["posinset"] = position.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
}
