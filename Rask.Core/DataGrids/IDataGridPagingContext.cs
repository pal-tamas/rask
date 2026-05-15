namespace Rask.Core.DataGrids;

// Non-generic facet of DataGridContext<TRow> exposing only paging state. DataGridPager
// resolves the context through this interface so it doesn't need to know TRow.
public interface IDataGridPagingContext
{
    int CurrentPage { get; }
    int PageCount { get; }
    int TotalCount { get; }
    int PageSize { get; }

    void GoToPage(int page);
    void NextPage();
    void PreviousPage();

    event Action? Changed;
}
