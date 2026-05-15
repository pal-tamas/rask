namespace Rask.Core.DataGrids;

// Ambient scope for the active DataGrid<TRow>. Mirrors EditContextScope so descendant
// helpers (DataGridRows, DataGridSortButton, DataGridPager) can locate the grid's state
// without taking it as a prop. Stored as object? because the underlying context is
// generic — typed access goes through CurrentAs<TRow>().
public static class DataGridScope
{
    private static readonly AsyncLocal<object?> _current = new();

    public static object? Current => _current.Value;

    public static DataGridContext<TRow>? CurrentAs<TRow>() => _current.Value as DataGridContext<TRow>;

    public static IDataGridPagingContext? CurrentPaging => _current.Value as IDataGridPagingContext;

    internal static IDisposable Push(object ctx)
    {
        var prev = _current.Value;
        _current.Value = ctx;
        return new Popper(prev);
    }

    private sealed class Popper(object? prev) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _current.Value = prev;
        }
    }
}
