using System.Linq.Expressions;
using System.Numerics;

namespace Rask;

/// <summary>
///     <c>Ui.Filter.Range(() =&gt; WeightFrom, () =&gt; WeightTo)</c>: a filter of a least and a most, in a
///     <see cref="UiFilters" />.
/// </summary>
public static class UiFilterRange
{
    /// <summary>
    ///     A range of numbers, its two ends side by side in the set's panel — either may be left empty — and one badge
    ///     that says what is set: "Weight: at least 5,000", "at most 7,000", "5,000 – 7,000", in the reader's grouping.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Each end is a page member of a nullable number with <c>[QueryParam]</c>, kept under its own address key and
    ///         written once typing pauses, as a <c>Ui.Input</c> in the set is. The badge's X, and Clear, take both out in
    ///         one move. Called by its <c>.Label(…)</c>, or the first end's <c>[Display]</c> name without one.
    ///     </para>
    ///     <code>
    ///     Ui.Filters[Ui.Filter.Range(() =&gt; WeightFrom, () =&gt; WeightTo).Label("Weight")]
    ///     </code>
    ///     <para>
    ///         The words are <see cref="RaskString.FilterRangeAtLeast" />, <see cref="RaskString.FilterRangeAtMost" /> and
    ///         <see cref="RaskString.FilterRangeBetween" />; the fields say <see cref="RaskString.FilterRangeFrom" /> and
    ///         <see cref="RaskString.FilterRangeTo" />. Not Flux's: the owner's (2026-11-18).
    ///     </para>
    /// </remarks>
    /// <param name="filter">The filter's entry.</param>
    /// <param name="from">The least: the page's member the address fills.</param>
    /// <param name="to">The most: the page's member the address fills.</param>
    /// <typeparam name="T">The number: <c>int</c>, <c>long</c>, <c>decimal</c>, <c>double</c>.</typeparam>
    /// <returns>The filter.</returns>
    public static UiFilter Range<T>(this RaskSeed_UiFilter filter, Expression<Func<T?>> from, Expression<Func<T?>> to)
        where T : struct, INumber<T>
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        var ends = new UiFilterEnds<T>(from, to);
        return filter.Label(ends.Name).Ranging(ends);
    }
}
