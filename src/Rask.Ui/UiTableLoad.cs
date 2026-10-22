using Rask.Querying;

namespace Rask;

/// <summary>
///     The load state of a table whose page fills its rows itself (<see cref="UiTable.Loading" />), read as a query's
///     is: it has no rows to keep on screen meanwhile.
/// </summary>
internal sealed class UiTableLoad : IQueryState
{
    /// <inheritdoc />
    public bool IsLoading { get; set; }

    /// <inheritdoc />
    public bool IsPlaceholderData => false;
}
