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
}
