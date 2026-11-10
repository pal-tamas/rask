using System.Globalization;
using Rask.Core.Live;
using Rask.Core.Virtualization;

namespace Rask;

// A long list draws the rows its scroll shows, and no others (Core's virtualization, VirtualScroll).
//
// Not Flux's: Flux draws every option, and so does a list here of up to UiListWindow.Above of them — what every example
// on Flux's pages is, so parity holds. Past that the page draws a window: the rows the scroll box shows, a few more on
// either side, and two spacers that keep the box as tall as the whole list, so the reader scrolls through all of it
// without typing. Every row is as tall as the next, so where a row is follows from its place alone.
//
// What the browser cannot do with rows it does not have is done here: the search narrows the rows (UiListSearch, the
// rules rask-list.ts keeps for a list it holds whole), type-ahead finds a row past the window and moves the window to
// it. What it still does is the rest: the keys walk the rows, and at the window's edge scroll it on (rask-list.ts,
// `data-rask-list-window`), and the row a key lands on is lit once the page has drawn it.
public abstract partial class UiSelectControl<T>
{
    private readonly VirtualScroll _scroll = new();

    // Windowed once the rows outnumbered UiListWindow.Above, and from then on: a list that changed shape under a search
    // would close, its popup drawn anew.
    private bool _windowed;

    // What the rows on offer were drawn for — the search, the answer — and how many times that has changed: a new set of
    // rows starts at the top, and the browser hears so (`data-rask-list-window`).
    private string? _shownFor;

    private int _generation;

    // How many answers a search the page answers has brought: each is a new set of rows.
    private int _answers;

    // The row type-ahead last found in an open list, and the question it answers.
    private int _soughtSeq;

    private int _soughtAt = -1;

    /// <summary>Whether the select narrows a windowed list itself: no search of the page's, and filtering not turned off.</summary>
    private bool FiltersItself => _windowed && Filter != false && !Asks;

    // A drawn list of one shape of row past the threshold. Groups and the page's own children are walked by position,
    // and a list that holds them is drawn whole.
    private void DecideWindow(Parts parts)
    {
        _windowed = _windowed
                    || (parts.Rows.Count > UiListWindow.Above
                        && parts.Blocks.TrueForAll(block => block.Group is null && block.Other is null));
    }

    // The rows on offer: every one, or those the search leaves in a list the select narrows itself.
    private List<Row> Shown(Parts parts, IReadOnlyList<T> picked, string search)
    {
        if (!_windowed)
        {
            return parts.Rows;
        }

        var needle = FiltersItself ? UiListSearch.Fold(Searched(parts, picked, search)) : string.Empty;
        var shown = needle.Length == 0 ? parts.Rows : parts.Rows.FindAll(row => UiListSearch.Finds(row.Option, needle));
        var drawnFor = needle + "\n" + _answers.ToString(CultureInfo.InvariantCulture);
        if (!string.Equals(drawnFor, _shownFor, StringComparison.Ordinal))
        {
            _shownFor = drawnFor;
            _generation++;
            _scroll.Reset();
        }

        return shown;
    }

    // What the list is searched for. A combobox's input says its answer again once the list shuts, and is told the page
    // so: words that name the answer are no search.
    private string Searched(Parts parts, IReadOnlyList<T> picked, string search) =>
        IsCombobox && parts.Rows.Exists(row => picked.Contains(row.Value) && string.Equals(row.Option.SelectedText, search, StringComparison.Ordinal))
            ? string.Empty
            : search;

    // The rows of a windowed list as the scroll box shows them; null for a list drawn whole.
    private VirtualWindow? WindowOf(List<Row> shown, int rowHeight) =>
        _windowed ? _scroll.Window(rowHeight, UiListWindow.Overscan, shown.Count, UiListWindow.Height) : null;

    // Every row of a windowed list is as tall as the next: the taller one when any of them has a line under its words.
    private static int RowHeightOf(Parts parts) =>
        parts.Rows.Exists(row => row.Option.Description is not null) ? UiListWindow.TallRow : UiListWindow.Row;

    // The scroll box said where it is: the next render draws the rows there.
    private void Scrolled(Event e) => _scroll.Read(e);

    // What the browser reads a windowed list by (rask-list.ts): how tall a row is and which set of rows these are, where the
    // first picked row is among them, and the row type-ahead found.
    private Dictionary<string, string?> WindowMarks(View view)
    {
        var marks = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["data-rask-list-window"] = string.Join(
                ' ',
                view.RowHeight.ToString(CultureInfo.InvariantCulture),
                _generation.ToString(CultureInfo.InvariantCulture),
                view.Shown.Count.ToString(CultureInfo.InvariantCulture)),
        };
        if (view.PickedAt >= 0)
        {
            marks["data-rask-list-picked"] = view.PickedAt.ToString(CultureInfo.InvariantCulture);
        }

        if (_soughtSeq > 0)
        {
            marks["data-rask-list-sought"] = _soughtSeq.ToString(CultureInfo.InvariantCulture) + " " + _soughtAt.ToString(CultureInfo.InvariantCulture);
        }

        return marks;
    }

    // The rows the window holds, between the room the rows above and below it would take.
    private IEnumerable<Component?> WindowRows(View view, VirtualWindow window)
    {
        yield return Spacer("ui:before", window.OffsetBefore);
        for (var at = window.Start; at < window.End; at++)
        {
            yield return OptionRow(view, view.Shown[at], at);
        }

        yield return Spacer("ui:after", window.OffsetAfter);
    }

    private static HTMLDivElement Spacer(string key, int height) =>
        Div.Key(key).Style("height:" + height.ToString(CultureInfo.InvariantCulture) + "px").Aria("hidden", "true");

    // What type-ahead tells the page: a field the browser writes the letters into (rask-list.ts `seek`), once the list is
    // windowed and the row they land on may not be drawn.
    private HTMLInputElement<string>? SeekField(View view) =>
        view.Window is null || LiveRenderContext.CurrentSync is not { } live
            ? null
            : Input.Key("seek").Value(string.Empty).Type(InputType.Hidden).Attributes(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["data-rask-list-seek"] = null,
                ["data-rask-on-input"] = live.RegisterHandler((Func<string, Task>)SeekAsync),
            });

    // "<question>\n<1 open, 0 shut>\n<the cursor's row, or -1>\n<the letters so far>\n<the last of them>". In an open list the
    // window moves to the row they land on and the browser lights it; in a shut one it is picked, as a native select's is.
    private Task SeekAsync(string said)
    {
        var parts = said.Split('\n');
        if (parts.Length != 5
            || parts[3].Length > UiListWindow.MaxTyped
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var seq)
            || !int.TryParse(parts[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var cursor))
        {
            return Task.CompletedTask;
        }

        var view = Latest;
        var open = string.Equals(parts[1], "1", StringComparison.Ordinal);
        var texts = view.Shown.ConvertAll(row => row.Option.Disabled == true ? null : row.Option.Text);
        var from = Math.Clamp(open ? cursor : view.PickedAt, -1, view.Shown.Count - 1);
        var hit = UiListSearch.TypeAhead(parts[3], parts[4], from, texts);
        if (hit < 0)
        {
            return Task.CompletedTask;
        }

        if (!open)
        {
            return PickedAsync(view.Shown[hit].Value);
        }

        _soughtSeq = seq;
        _soughtAt = hit;
        _scroll.Reveal(hit, view.RowHeight, UiListWindow.Height);

        return Task.CompletedTask;
    }
}
