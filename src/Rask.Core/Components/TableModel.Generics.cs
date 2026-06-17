using System.Diagnostics.CodeAnalysis;
using Rask.Core.Live;
using Rask.Core.Tables;

namespace Rask.Core.Components;

// Hand-written factory for the [SkipFactory] TableModel<T> component. Lives in the same
// `partial class Generated` the generator emits the other Rask.Core.Components factories into, and
// reproduces the generated factory shape (GetOrCreate via the live context + re-apply props +
// NotifyParameters) so reconciliation and lifecycle behave identically to a generated component.
//
// The OnSort/OnPage/OnSelect (+ async) delegates are wrapped with AutoCallback.Wrap so that invoking
// one — from a header click, pager button, or row checkbox — re-renders the host that supplied it.
// That re-render is the whole controlled loop: the host mutates its own state in the callback, then
// renders again with the new Sort/PageIndex/SelectedKeys props. The Render delegate
// (Func<TableModelContext<T>, Component>) is deliberately NOT wrapped — only true event callbacks are.
public static partial class Generated
{
    [UnconditionalSuppressMessage("Trimming", "IL2091",
        Justification = "T flows only through generic instantiation and user-supplied delegates; " +
                        "no reflection, no DynamicInvoke.")]
    public static TableModel<T> TableModel<T>(
        Func<TableModelContext<T>, Component> Render,
        IReadOnlyList<ColumnDef<T>> Columns,
        IReadOnlyList<T> Rows,
        Func<T, object>? KeySelector = null,
        IReadOnlyList<ColumnSort>? Sort = null,
        int PageIndex = 0,
        int PageCount = 1,
        int PageSize = 0,
        int TotalRowCount = 0,
        IReadOnlyCollection<object>? SelectedKeys = null,
        bool MultiSort = false,
        Action<IReadOnlyList<ColumnSort>>? OnSort = null,
        Func<IReadOnlyList<ColumnSort>, Task>? OnSortAsync = null,
        Action<int>? OnPage = null,
        Func<int, Task>? OnPageAsync = null,
        Action<IReadOnlyCollection<object>>? OnSelect = null,
        Func<IReadOnlyCollection<object>, Task>? OnSelectAsync = null,
        object? Key = null)
    {
        ArgumentNullException.ThrowIfNull(Render);

        var onSort = AutoCallback.Wrap(OnSort);
        var onSortAsync = AutoCallback.Wrap(OnSortAsync);
        var onPage = AutoCallback.Wrap(OnPage);
        var onPageAsync = AutoCallback.Wrap(OnPageAsync);
        var onSelect = AutoCallback.Wrap(OnSelect);
        var onSelectAsync = AutoCallback.Wrap(OnSelectAsync);

        TableModel<T> Apply(TableModel<T> c)
        {
            c.Body = Render;
            c.Columns = Columns;
            c.Rows = Rows;
            c.KeySelector = KeySelector;
            c.Sort = Sort;
            c.PageIndex = PageIndex;
            c.PageCount = PageCount;
            c.PageSize = PageSize;
            c.TotalRowCount = TotalRowCount;
            c.SelectedKeys = SelectedKeys;
            c.MultiSort = MultiSort;
            c.OnSort = onSort;
            c.OnSortAsync = onSortAsync;
            c.OnPage = onPage;
            c.OnPageAsync = onPageAsync;
            c.OnSelect = onSelect;
            c.OnSelectAsync = onSelectAsync;
            c.Key = Key;
            return c;
        }

        if (LiveRenderContext.Current is { } ctx)
        {
            var component = ctx.GetOrCreate<TableModel<T>>(static _ => new TableModel<T>());
            Apply(component);
            // Presentational: the projection is recomputed from whatever props the host passes this
            // render, so always treat params as changed — the parent only re-invokes this factory when
            // it re-renders, and a fresh projection per render is trivial.
            ctx.NotifyParameters(component, true);
            return component;
        }

        return Apply(new TableModel<T>());
    }
}
