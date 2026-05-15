using Rask.Core.DataGrids;

namespace Rask.Core.Components;

// Headless data grid: emits no DOM of its own. Owns a DataGridContext<TRow> that tracks
// sort rules + current page across renders, and pushes it onto DataGridScope so descendant
// helpers (DataGridRows, DataGridSortButton, DataGridPager) can read the state without
// taking it as a prop. The consumer composes their own Table/Thead/Tr/Td markup.
public sealed class DataGrid<TRow> : Component
{
    private DataGridContext<TRow>? _context;
    private IEnumerable<TRow>? _lastSource;
    private int _lastPageSize;

    public IEnumerable<TRow>? Source { get; set; }
    public int PageSize { get; set; }

    protected override Component Render() => new Fragment(Children ?? Array.Empty<Child>());

    // Reconciles the cached DataGridContext against the latest Source/PageSize, then pushes
    // it onto DataGridScope for descendant helpers. Compares the *original* Source reference
    // (not the materialised list) so a caller that reuses the same IEnumerable across renders
    // doesn't trigger a paging reset.
    protected override IDisposable? EnterChildrenScope()
    {
        var list = Source as IReadOnlyList<TRow>
                   ?? Source?.ToList() as IReadOnlyList<TRow>
                   ?? Array.Empty<TRow>();

        if (_context is null)
        {
            _context = new DataGridContext<TRow>(list, PageSize);
            _context.Changed += StateHasChanged;
        }
        else
        {
            if (!ReferenceEquals(Source, _lastSource))
            {
                _context.ReplaceSource(list);
            }

            if (PageSize != _lastPageSize)
            {
                _context.PageSize = PageSize;
            }
        }

        _lastSource = Source;
        _lastPageSize = PageSize;

        return DataGridScope.Push(_context);
    }
}
