namespace Rask.Core.DataGrids;

public sealed class DataGridContext<TRow> : IDataGridPagingContext
{
    private readonly List<SortRule<TRow>> _sortRules = new();
    private IReadOnlyList<TRow> _source;
    private int _pageSize;
    private int _currentPage;

    public DataGridContext(IReadOnlyList<TRow> source, int pageSize)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _pageSize = Math.Max(0, pageSize);
        _currentPage = 0;
    }

    public IReadOnlyList<TRow> Source => _source;

    public int PageSize
    {
        get => _pageSize;
        set
        {
            var clamped = Math.Max(0, value);
            if (clamped == _pageSize)
            {
                return;
            }

            _pageSize = clamped;
            ClampPage();
            RaiseChanged();
        }
    }

    public int CurrentPage => _currentPage;

    public int TotalCount => _source.Count;

    // Empty source still reports 1 page so the pager UI renders "Page 1 of 1" instead of
    // "1 of 0" or dividing by zero.
    public int PageCount
    {
        get
        {
            if (_pageSize <= 0 || _source.Count == 0)
            {
                return 1;
            }

            return (_source.Count + _pageSize - 1) / _pageSize;
        }
    }

    public IReadOnlyList<SortRule<TRow>> SortRules => _sortRules;

    public event Action? Changed;

    // Sorted + paged view of Source. Stable sort via OrderBy/ThenBy chain. When no rules
    // are registered, returns the original Source order.
    public IEnumerable<TRow> VisibleRows
    {
        get
        {
            IEnumerable<TRow> seq = _source;
            if (_sortRules.Count > 0)
            {
                var first = _sortRules[0];
                IOrderedEnumerable<TRow> ordered = first.Descending
                    ? seq.OrderByDescending(first.Selector, NullSafeComparer.Instance)
                    : seq.OrderBy(first.Selector, NullSafeComparer.Instance);
                for (var i = 1; i < _sortRules.Count; i++)
                {
                    var rule = _sortRules[i];
                    ordered = rule.Descending
                        ? ordered.ThenByDescending(rule.Selector, NullSafeComparer.Instance)
                        : ordered.ThenBy(rule.Selector, NullSafeComparer.Instance);
                }

                seq = ordered;
            }

            if (_pageSize > 0)
            {
                seq = seq.Skip(_currentPage * _pageSize).Take(_pageSize);
            }

            return seq;
        }
    }

    // Three-state cycle on non-additive clicks:
    //   no rule    → asc
    //   asc        → desc
    //   desc       → remove (back to unsorted)
    // Additive (shift-click) clicks append a new rule, or flip an existing one in place.
    public void ToggleSort(string key, Func<TRow, object?> selector, bool additive)
    {
        if (string.IsNullOrEmpty(key)) throw new ArgumentException("Sort key required.", nameof(key));
        if (selector is null) throw new ArgumentNullException(nameof(selector));

        var existingIndex = -1;
        for (var i = 0; i < _sortRules.Count; i++)
        {
            if (_sortRules[i].Key == key)
            {
                existingIndex = i;
                break;
            }
        }

        if (additive)
        {
            if (existingIndex < 0)
            {
                _sortRules.Add(new SortRule<TRow>(key, false, selector));
            }
            else
            {
                var existing = _sortRules[existingIndex];
                if (!existing.Descending)
                {
                    _sortRules[existingIndex] = existing with { Descending = true, Selector = selector };
                }
                else
                {
                    _sortRules.RemoveAt(existingIndex);
                }
            }
        }
        else
        {
            if (existingIndex < 0 || _sortRules.Count != 1)
            {
                _sortRules.Clear();
                _sortRules.Add(new SortRule<TRow>(key, false, selector));
            }
            else
            {
                var existing = _sortRules[0];
                if (!existing.Descending)
                {
                    _sortRules[0] = existing with { Descending = true, Selector = selector };
                }
                else
                {
                    _sortRules.Clear();
                }
            }
        }

        RaiseChanged();
    }

    public void GoToPage(int page)
    {
        var clamped = Math.Clamp(page, 0, Math.Max(0, PageCount - 1));
        if (clamped == _currentPage)
        {
            return;
        }

        _currentPage = clamped;
        RaiseChanged();
    }

    public void NextPage() => GoToPage(_currentPage + 1);

    public void PreviousPage() => GoToPage(_currentPage - 1);

    // Called when the parent DataGrid<TRow> sees a fresh Source reference. Preserves sort
    // rules (caller likely re-fetched the same logical collection) and clamps the current
    // page if the new source has fewer pages.
    public void ReplaceSource(IReadOnlyList<TRow> next)
    {
        _source = next ?? throw new ArgumentNullException(nameof(next));
        ClampPage();
        RaiseChanged();
    }

    private void ClampPage()
    {
        var max = Math.Max(0, PageCount - 1);
        if (_currentPage > max)
        {
            _currentPage = max;
        }
    }

    private void RaiseChanged() => Changed?.Invoke();

    // Default Comparer<object> on .NET throws when the runtime type isn't IComparable; we
    // wrap it so missing values (null) sort consistently before non-null values and avoid
    // the box-dance on every comparison.
    private sealed class NullSafeComparer : IComparer<object?>
    {
        public static readonly NullSafeComparer Instance = new();

        public int Compare(object? x, object? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            return Comparer<object>.Default.Compare(x, y);
        }
    }
}
