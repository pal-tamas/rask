namespace Rask.Ui;

/// <summary>
/// What a <see cref="UiDataGrid{T,TKey}.Source" /> hands back: the rows of one page, and how many rows stand
/// behind it.
/// </summary>
/// <param name="Rows">The page's rows, already ordered and already sliced.</param>
/// <param name="Total">
///     How many rows the whole set holds, which is what the pager counts in. A source that returns the
///     page's own length here renders a grid that always claims to be one page long.
/// </param>
/// <typeparam name="T">The row type.</typeparam>
public sealed record UiGridPage<T>(IReadOnlyList<T> Rows, int Total);
