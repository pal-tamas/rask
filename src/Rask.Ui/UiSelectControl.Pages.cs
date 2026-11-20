namespace Rask;

// A search that pages (`.Choices((typed, page) => …)`, #1354): the first page answers what is typed, as any search's
// answer does (UiSelectControl.Search), and the list goes on as the reader scrolls. Once the window the box shows reaches
// the last row found so far, the next page is asked for and its rows are put after the others — the same set of rows,
// so the box stays where the reader has it and the cursor on its row. A page of fewer rows than asked for is the end.
//
// A source that says how many rows it has (Ui.ChoiceRows, owner 2026-11-20: "the scrollbar's real size, native-feeling")
// is drawn that long from its first page: the rows not found yet are skeleton rows under the spacer after the window
// (UiSelectControl.Window), so the scrollbar is the whole list's and stays still as pages arrive. A reader who drags it
// far is answered with one question for every row up to where the box shows — not a page at a time behind them — at
// most UiListWindow.MostAsked rows each; one that does not say has a box of skeleton rows past the last row found while
// more may follow, so a scroll to the end runs on into rows on their way instead of stopping.
//
// Latest words win (#1344, #1350): a page answers the words the rows on offer were found for, and is dropped when
// something else has been asked since, or when the rows it would follow are no longer the ones it was asked after.
public abstract partial class UiSelectControl<T>
{
    // Whether the rows found so far may not be all: the source has more, or the last page came back full.
    private bool _more;

    // How many rows the source has past those found so far, when it says (Ui.ChoiceRows); null when it does not.
    private int? _left;

    // Which question the rows found so far answer, and the words it asked.
    private int _answered;

    private string _answeredFor = string.Empty;

    // Whether a page is out: one at a time.
    private bool _paging;

    /// <summary>
    ///     The next page of the search that pages — the select's own <c>.Choices(…)</c>, or the one a pillbox hands its list
    ///     with that search — and null for any other search, the page's own <c>Search</c> among them.
    /// </summary>
    private Func<string, int, int, Task<IEnumerable<Component?>>>? Pager => Search is null ? Choosing?.Next : Pills?.Next;

    /// <summary>Whether the rows on offer come a page at a time: such a list is windowed from its first page on.</summary>
    private bool Pages => Pager is not null;

    /// <summary>
    ///     The rows a windowed list counts past those found so far: as many as the source says it has, a box of them while
    ///     one that does not say may have more, and none once the last is here.
    /// </summary>
    private int Unfound => Pages && _more ? _left ?? UiListWindow.Overscan : 0;

    // The first page is here: the rows on offer are its own, and more may follow when the source has more, or when a
    // source that does not say came back full.
    private void FirstPage(int asked, string typed, int count, IEnumerable<Component?>? answer)
    {
        _answered = asked;
        _answeredFor = typed;
        TakeLeft(answer, count, UiListWindow.Page);
    }

    private void TakeLeft(IEnumerable<Component?>? answer, int count, int take)
    {
        _left = Pages && (answer as UiPageAnswer)?.Left is { } said ? Math.Min(said, UiListWindow.MostCounted) : null;
        _more = Pages && (_left is { } left ? left > 0 && count > 0 : count >= take);
    }

    // Asks for the rows the window reaches past the last row found so far, again while the reader's scroll, heard
    // meanwhile, still reaches past the rows a page brought.
    private async Task NextPageAsync()
    {
        if (_paging)
        {
            return;
        }

        _paging = true;
        try
        {
            var going = true;
            while (going && NextTake() is { } take)
            {
                going = await PageAsync(take).ConfigureAwait(false);
            }
        }
        finally
        {
            _paging = false;
        }
    }

    // How many rows the next page asks for — those up to where the window reaches, in whole pages — or null while the
    // window, and the overscan past it, stays clear of the rows not found yet.
    private int? NextTake()
    {
        if (!_more || _answered != _asked || Pager is null || _found is not { } found || _latest is not { } view)
        {
            return null;
        }

        var offered = view.Shown.Count + (found.Length - view.FoundCount);
        var end = _scroll.Window(view.RowHeight, UiListWindow.Overscan, offered + Unfound, UiListWindow.Height).End;
        if (end < offered)
        {
            return null;
        }

        var pages = (end - offered + UiListWindow.Page - 1) / UiListWindow.Page;
        return Math.Clamp(pages * UiListWindow.Page, UiListWindow.Page, UiListWindow.MostAsked);
    }

    // One page, put after the rows found so far; false once it was dropped, an answer to older words.
    private async Task<bool> PageAsync(int take)
    {
        if (Pager is not { } pager || _found is not { } found)
        {
            return false;
        }

        var asked = _asked;
        var skip = found.Length;
        var answer = pager(_answeredFor.Trim(), skip, take) is { } pending ? await pending.ConfigureAwait(false) : null;
        if (asked != _asked || _found is not { } now || now.Length != skip)
        {
            return false;
        }

        Component?[] rows = [.. (answer ?? []).Select(AsRow)];
        _found = [.. now, .. rows];
        TakeLeft(answer, rows.Length, take);
        return true;
    }

    // How many rows a windowed row says there are: every one the source has, when it says; unknown (-1, as ARIA says it)
    // while more pages of one that does not may follow.
    private int SetSize(View view)
    {
        if (!_more)
        {
            return view.Shown.Count;
        }

        return _left is { } left ? view.Shown.Count + left : -1;
    }

    // The rows a windowed list's keys walk: those on offer, and those the source says it has past them.
    private int Walked(View view) => view.Shown.Count + (_more ? _left ?? 0 : 0);
}
