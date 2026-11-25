namespace Rask;

/// <summary>
///     Where a picker over a table gets its rows: a page at a time for what was typed, and one row by its id. A read face
///     hands one out — <c>Company.Choices(c =&gt; c.Name)</c> — searching and ordering in the database, through the tenant
///     in flight.
/// </summary>
/// <typeparam name="TKey">The id's type.</typeparam>
/// <remarks>
///     A source never names a row the reader may not see: an id of another tenant's row, or of none at all, is answered
///     with nothing, by <see cref="Find" /> and in <see cref="ChoiceRowPage{TKey}.Picked" /> alike.
/// </remarks>
public interface IChoiceSource<TKey>
{
    /// <summary>
    ///     The page of rows whose words hold <paramref name="typed" /> — every row while nothing is typed — that
    ///     <paramref name="skip" /> and <paramref name="take" /> name, with how many there are and the
    ///     <paramref name="picked" /> rows.
    /// </summary>
    /// <param name="typed">What the reader typed; empty or white space for every row.</param>
    /// <param name="skip">How many rows of the search come before the page.</param>
    /// <param name="take">How many rows the page holds at most.</param>
    /// <param name="picked">The ids the picker holds, named on every page; null or empty for none.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The page, the total, and the picked rows.</returns>
    Task<ChoiceRowPage<TKey>> Page(
        string typed, int skip, int take, IReadOnlyCollection<TKey>? picked = null, CancellationToken cancellationToken = default);

    /// <summary>The row <paramref name="id" /> names, or null when the reader has no such row.</summary>
    /// <param name="id">The id.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The row, or null.</returns>
    Task<ChoiceRow<TKey>?> Find(TKey id, CancellationToken cancellationToken = default);
}
