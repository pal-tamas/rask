namespace Rask;

// A search that pages (`.Choices((typed, page) => …)`, #1354): the first page answers what is typed, as any search's
// answer does (UiSelectControl.Search), and the list goes on as the reader scrolls. Once the window the box shows reaches
// the last row found so far, the next page is asked for and its rows are put after the others — the same set of rows,
// so the box stays where the reader has it and the cursor on its row. A page of fewer rows than asked for is the end.
//
// Latest words win (#1344, #1350): a page answers the words the rows on offer were found for, and is dropped when
// something else has been asked since, or when the rows it would follow are no longer the ones it was asked after.
public abstract partial class UiSelectControl<T>
{
    // Whether the rows found so far may not be all: the last page came back full.
    private bool _more;

    // Which question the rows found so far answer, and the words it asked.
    private int _answered;

    private string _answeredFor = string.Empty;

    // Whether a page is out: one at a time.
    private bool _paging;

    /// <summary>
    ///     The next page of the search that pages — the select's own <c>.Choices(…)</c>, or the one a pillbox hands its list
    ///     with that search — and null for any other search, the page's own <c>Search</c> among them.
    /// </summary>
    private Func<string, int, Task<IEnumerable<Component?>>>? Pager => Search is null ? Choosing?.Next : Pills?.Next;

    /// <summary>Whether the rows on offer come a page at a time: such a list is windowed from its first page on.</summary>
    private bool Pages => Pager is not null;

    // The first page is here: the rows on offer are its own, and more may follow when it was full.
    private void FirstPage(int asked, string typed, int count)
    {
        _answered = asked;
        _answeredFor = typed;
        _more = Pages && count >= UiListWindow.Page;
    }

    // Asks for the next page once the window reaches the last row found so far.
    private async Task NextPageAsync()
    {
        if (!_more || _paging || _answered != _asked || Pager is not { } pager || _found is not { } found || _latest is not { } view
            || !NearTheEnd(view))
        {
            return;
        }

        var asked = _asked;
        var skip = found.Length;
        _paging = true;
        try
        {
            var answer = pager(_answeredFor.Trim(), skip) is { } pending ? await pending : null;
            if (asked != _asked || _found is not { } now || now.Length != skip)
            {
                return;
            }

            Component?[] rows = [.. (answer ?? []).Select(AsRow)];
            _found = [.. now, .. rows];
            _more = rows.Length >= UiListWindow.Page;
        }
        finally
        {
            _paging = false;
        }
    }

    // Whether the rows the box shows, and the overscan past them, reach the last row on offer.
    private bool NearTheEnd(View view) =>
        _scroll.Window(view.RowHeight, UiListWindow.Overscan, view.Shown.Count, UiListWindow.Height).End >= view.Shown.Count;

    // How many rows a windowed row says there are: unknown (-1, as ARIA says it) while more pages may follow.
    private int SetSize(View view) => _more ? -1 : view.Shown.Count;
}
