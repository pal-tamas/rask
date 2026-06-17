namespace Rask.Core.Tables;

// Projection of one ColumnDef for the current render: the header text plus this column's place in the
// controlled sort state and a ToggleSort action that proposes the next sort (asc → desc → none) for it.
//   • Direction     — the column's current sort direction, or null when it is not in the sort list.
//   • SortPriority  — its index in the sort list (0 = primary), or -1 when not sorted (drives a
//                     multi-sort UI's "1, 2, 3" badges).
//   • ToggleSort    — proposes the next sort via OnSort; a no-op when the column is not Sortable or no
//                     OnSort/OnSortAsync callback was supplied.
public sealed record HeaderCell(
    string ColumnId,
    string Header,
    bool Sortable,
    SortDirection? Direction,
    int SortPriority,
    Action ToggleSort);
