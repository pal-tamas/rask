namespace Rask;

/// <summary>
///     One page of the rows a picker offers for what was typed, how many rows the search finds in all, and the picked
///     rows — named even when they are not on this page, so a picker shows what it holds before anything is found.
/// </summary>
/// <typeparam name="TKey">The id's type.</typeparam>
/// <param name="rows">The page, in the source's order.</param>
/// <param name="total">How many rows the search finds in all, the page's and every other page's.</param>
/// <param name="picked">The picked rows there are, in the order they were asked for; a picked id with no row is left out.</param>
public sealed class ChoiceRowPage<TKey>(IReadOnlyList<ChoiceRow<TKey>> rows, int total, IReadOnlyList<ChoiceRow<TKey>> picked)
{
    /// <summary>The page, in the source's order.</summary>
    public IReadOnlyList<ChoiceRow<TKey>> Rows { get; } = rows ?? throw new ArgumentNullException(nameof(rows));

    /// <summary>How many rows the search finds in all, never fewer than none.</summary>
    public int Total { get; } = Math.Max(0, total);

    /// <summary>The picked rows there are, whether or not they are on this page.</summary>
    public IReadOnlyList<ChoiceRow<TKey>> Picked { get; } = picked ?? throw new ArgumentNullException(nameof(picked));
}
