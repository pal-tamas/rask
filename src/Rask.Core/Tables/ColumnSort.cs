namespace Rask.Core.Tables;

// One entry in the controlled sort state: a column and the direction it is sorted in. The host owns
// the ordered list (first entry = primary key); TableModel only proposes the next list via OnSort and
// never stores it.
public readonly record struct ColumnSort(string ColumnId, SortDirection Direction);
