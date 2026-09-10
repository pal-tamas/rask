namespace Rask.Ui;

/// <summary>
/// What a <see cref="UiDataGrid{T}.Source" /> is asked for: one page, ordered one way.
/// </summary>
/// <param name="Sort">
///     The <see cref="UiColumn{T}.Field" /> token to order by, or <see langword="null" /> for the source's
///     own order.
/// </param>
/// <param name="Descending">Which way round.</param>
/// <param name="Page">The page wanted, counting from zero.</param>
/// <param name="PageSize">How many rows it holds, or <c>0</c> when the grid is not paging.</param>
/// <remarks>
///     A token and a page number rather than an expression tree, because a source is as likely to be an
///     HTTP call as a database query — and a request that only a LINQ provider can read would rule the
///     first one out.
/// </remarks>
public readonly record struct UiGridRequest(string? Sort, bool Descending, int Page, int PageSize);
