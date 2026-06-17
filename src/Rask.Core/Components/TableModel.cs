using Rask.Core.Tables;

namespace Rask.Core.Components;

// Headless, TanStack-Table-style table model. Fully *controlled* / presentational: it renders no DOM
// of its own, holds no state, and transforms no data. The host sorts and slices its own data, passes
// the final `Rows` plus the current view state (Sort / PageIndex / SelectedKeys / …) in as props, and
// TableModel:
//   • projects sort-aware HeaderCells and selection-aware TableRows into a TableModelContext<T>, and
//   • exposes pure handler actions (ToggleSort, SetPage, ToggleRow, …) that only *propose* the next
//     state by invoking the matching event (OnSort / OnPage / OnSelect) — they mutate nothing.
// The host applies the proposal to its own state and re-renders, and the new props flow back down.
//
// Because all inputs are props, there is no hidden state for the framework to miss — so unlike
// VirtualizeModel this needs no BypassRenderCache and no OnPropsChanged reset. The re-render after an event
// is the standard callback path: the OnSort/OnPage/OnSelect delegates are wrapped by AutoCallback in
// the factory, so invoking them re-renders the owning host (see TableModel.Generics.cs).
//
// Always invoked through the hand-written generic factory `Components.TableModel<T>(...)`
// (see TableModel.Generics.cs); the class carries [SkipFactory] so the generator emits no competing
// factory. The Render delegate is stored under `Body` to avoid colliding with Component.Render().
[SkipFactory]
public sealed class TableModel<T> : Component
{
    public IReadOnlyList<ColumnDef<T>>? Columns { get; set; }
    public IReadOnlyList<T>? Rows { get; set; }

    // The render fragment. Stored as "Body" rather than "Render" to avoid colliding with
    // Component.Render(); the factory exposes it to callers as "Render".
    public Func<TableModelContext<T>, Component>? Body { get; set; }

    // Optional row identity. When null, the row reference itself is the key.
    public Func<T, object>? KeySelector { get; set; }

    // ----- controlled state in -----
    public IReadOnlyList<ColumnSort>? Sort { get; set; }
    public int PageIndex { get; set; }
    public int PageCount { get; set; } = 1;
    public int PageSize { get; set; }
    public int TotalRowCount { get; set; }
    public IReadOnlyCollection<object>? SelectedKeys { get; set; }
    public bool MultiSort { get; set; }

    // ----- events out (wrapped by AutoCallback in the factory) -----
    public Action<IReadOnlyList<ColumnSort>>? OnSort { get; set; }
    public Func<IReadOnlyList<ColumnSort>, Task>? OnSortAsync { get; set; }
    public Action<int>? OnPage { get; set; }
    public Func<int, Task>? OnPageAsync { get; set; }
    public Action<IReadOnlyCollection<object>>? OnSelect { get; set; }
    public Func<IReadOnlyCollection<object>, Task>? OnSelectAsync { get; set; }

    protected override RenderResult Render()
    {
        if (Body is null)
        {
            throw new InvalidOperationException("TableModel: Render delegate is required.");
        }

        if (Columns is null || Columns.Count == 0)
        {
            throw new InvalidOperationException("TableModel: at least one ColumnDef is required.");
        }

        if (Rows is null)
        {
            throw new InvalidOperationException("TableModel: Rows is required.");
        }

        // Snapshot the controlled state for this render. The handler actions below close over these
        // snapshots, so each render's handlers propose changes relative to the state that produced them
        // — and the latest render's handlers are the ones the host's DOM wiring invokes.
        var columns = Columns;
        var rows = Rows;
        var sort = Sort ?? Array.Empty<ColumnSort>();
        var selected = SelectedKeys ?? Array.Empty<object>();
        var selectedLookup = selected as HashSet<object> ?? new HashSet<object>(selected);

        object KeyOf(T row) => KeySelector?.Invoke(row) ?? row!;

        int SortIndexOf(string columnId)
        {
            for (var i = 0; i < sort.Count; i++)
            {
                if (sort[i].ColumnId == columnId)
                {
                    return i;
                }
            }

            return -1;
        }

        void RaiseSort(IReadOnlyList<ColumnSort> next)
        {
            if (OnSort is not null)
            {
                OnSort(next);
            }
            else if (OnSortAsync is not null)
            {
                _ = OnSortAsync(next);
            }
        }

        void RaiseSelect(IReadOnlyCollection<object> next)
        {
            if (OnSelect is not null)
            {
                OnSelect(next);
            }
            else if (OnSelectAsync is not null)
            {
                _ = OnSelectAsync(next);
            }
        }

        void RaisePage(int next)
        {
            if (OnPage is not null)
            {
                OnPage(next);
            }
            else if (OnPageAsync is not null)
            {
                _ = OnPageAsync(next);
            }
        }

        void ToggleSort(string columnId)
        {
            ColumnDef<T>? col = null;
            foreach (var c in columns)
            {
                if (c.Id == columnId)
                {
                    col = c;
                    break;
                }
            }

            if (col is null || !col.Sortable)
            {
                return;
            }

            var idx = SortIndexOf(columnId);
            var current = idx >= 0 ? sort[idx].Direction : (SortDirection?)null;

            if (!MultiSort)
            {
                IReadOnlyList<ColumnSort> next = current switch
                {
                    null => new[] { new ColumnSort(columnId, SortDirection.Ascending) },
                    SortDirection.Ascending => new[] { new ColumnSort(columnId, SortDirection.Descending) },
                    _ => Array.Empty<ColumnSort>()
                };
                RaiseSort(next);
                return;
            }

            var list = new List<ColumnSort>(sort);
            if (idx < 0)
            {
                list.Add(new ColumnSort(columnId, SortDirection.Ascending));
            }
            else if (sort[idx].Direction == SortDirection.Ascending)
            {
                list[idx] = new ColumnSort(columnId, SortDirection.Descending);
            }
            else
            {
                list.RemoveAt(idx);
            }

            RaiseSort(list);
        }

        void SetPage(int target)
        {
            var max = Math.Max(0, PageCount - 1);
            var clamped = Math.Clamp(target, 0, max);
            if (clamped != PageIndex)
            {
                RaisePage(clamped);
            }
        }

        void ToggleRow(T row)
        {
            var key = KeyOf(row);
            var next = new HashSet<object>(selected);
            if (!next.Add(key))
            {
                next.Remove(key);
            }

            RaiseSelect(next);
        }

        void ToggleAll()
        {
            var keys = new List<object>(rows.Count);
            foreach (var row in rows)
            {
                keys.Add(KeyOf(row));
            }

            var next = new HashSet<object>(selected);
            var allSelected = keys.Count > 0 && keys.TrueForAll(next.Contains);
            foreach (var key in keys)
            {
                if (allSelected)
                {
                    next.Remove(key);
                }
                else
                {
                    next.Add(key);
                }
            }

            RaiseSelect(next);
        }

        var headers = new HeaderCell[columns.Count];
        for (var i = 0; i < columns.Count; i++)
        {
            var col = columns[i];
            var columnId = col.Id;
            var sortIdx = col.Sortable ? SortIndexOf(columnId) : -1;
            var direction = sortIdx >= 0 ? sort[sortIdx].Direction : (SortDirection?)null;
            headers[i] = new HeaderCell(
                columnId,
                col.Header ?? columnId,
                col.Sortable,
                direction,
                sortIdx,
                () => ToggleSort(columnId));
        }

        var projectedRows = new TableRow<T>[rows.Count];
        var allRowsSelected = rows.Count > 0;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var key = KeyOf(row);
            var isSelected = selectedLookup.Contains(key);
            allRowsSelected &= isSelected;
            projectedRows[i] = new TableRow<T>(row, key, i, isSelected, () => ToggleRow(row));
        }

        var context = new TableModelContext<T>
        {
            Headers = headers,
            Rows = projectedRows,
            Sort = sort,
            ToggleSort = ToggleSort,
            ClearSort = () => RaiseSort(Array.Empty<ColumnSort>()),
            PageIndex = PageIndex,
            PageCount = PageCount,
            PageSize = PageSize,
            TotalRowCount = TotalRowCount,
            HasPrevPage = PageIndex > 0,
            HasNextPage = PageIndex < PageCount - 1,
            SetPage = SetPage,
            NextPage = () => SetPage(PageIndex + 1),
            PrevPage = () => SetPage(PageIndex - 1),
            SelectedKeys = selected,
            IsSelected = row => selectedLookup.Contains(KeyOf(row)),
            ToggleRow = ToggleRow,
            ToggleAll = ToggleAll,
            AllSelected = allRowsSelected
        };

        return Body(context);
    }
}
