namespace Rask.Core.Tables;

// Direction a column is sorted in. "Unsorted" is represented by the column's absence from the sort
// list, not a third enum member — so the asc → desc → none cycle removes the entry on the third step.
public enum SortDirection
{
    Ascending,
    Descending
}
