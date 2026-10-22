using Rask.Querying;

namespace Rask;

/// <summary>
///     A table drawn from its rows' query and the queries it also waits for (<c>Ui.Table.Of(rows, columns)</c>): loading
///     while any of them is, and showing what the rows' query holds.
/// </summary>
internal sealed class UiTableQueries(IQueryState rows, params IQueryState[] others) : IQueryState
{
    /// <summary>The query the rows come from, which a count of rows shown is kept against.</summary>
    internal IQueryState Rows => rows;

    /// <inheritdoc />
    public bool IsLoading => rows.IsLoading || Array.Exists(others, other => other.IsLoading);

    /// <inheritdoc />
    public bool IsPlaceholderData => rows.IsPlaceholderData;
}
