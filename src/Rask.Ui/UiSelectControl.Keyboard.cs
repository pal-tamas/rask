using Rask.Core;

namespace Rask;

// What the keyboard and the pointer do, as recorded on Flux's page. The arrows stop at either end, Home, End
// and the page keys do nothing, a letter typed on the closed button picks, and Tab leaves and closes.
public abstract partial class UiSelectControl<T>
{
    // What is typed: into the listbox's search field, or into the combobox since it last showed its answer.
    private string _search = string.Empty;

    // The combobox's input shows what is being typed rather than the picked option's words.
    private bool _typing;

    // A handler of the page's is running: a search it answers, or the option it is creating.
    private bool _busy;

#pragma warning disable S3459 // a mutable struct whose default is its empty state; Next() fills it in place
    private UiTypeAhead _typeAhead;
#pragma warning restore S3459

    // Where the pointer last was on the list. An open list sends `mouseenter` to the row that appears under a
    // pointer that has not moved; Flux's cursor follows the pointer only when it does.
    private (int X, int Y)? _pointer;

    private readonly ElementRef<HTMLInputElement> _input = new();

    private readonly ElementRef<HTMLInputElement> _searchInput = new();

    private string ListId => HasSearchField ? Prefixed + "-list" : PanelId;

    private void Hovered(MouseEvent e, int index)
    {
        if (_pointer != (e.ClientX, e.ClientY))
        {
            _pointer = (e.ClientX, e.ClientY);
            _cursor = index;
        }
    }

    private Task ClickedAsync(PointerEvent e, int index, View view)
    {
        _pointer = (e.ClientX, e.ClientY);

        return PickAsync(index, view);
    }

    // The runtime leaves the text of an input that is being typed into to its reader, so a render cannot
    // change what a focused input says. Its text is put there through the element itself.
    private static async Task SayAsync(ElementRef<HTMLInputElement> input, string text)
    {
        // The whole text, however long it is: a range past the end stops at the end.
        await ReachAsync(() => input.SetRangeText(text, 0, int.MaxValue)).ConfigureAwait(false);
    }

    private static async Task ReachAsync(Func<ValueTask> call)
    {
        try
        {
            await call().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // No browser behind this render — a prerender, a test's page — so no element to reach. What the
            // render wrote is then all there is, and it says the same.
        }
    }

    // A click in the combobox opens it; with an answer showing, the answer is selected, ready to type over.
    private async Task OnInputClickAsync(View view, bool focuses = false)
    {
        _open = true;
        if (focuses)
        {
            await ReachAsync(() => _input.Focus()).ConfigureAwait(false);
        }

        if (view.Picked.Count > 0 && !_typing)
        {
            await ReachAsync(() => _input.Select()).ConfigureAwait(false);
        }
    }

    // The browser opened or closed the popover — a click on the button, Escape, a click elsewhere — or C# did.
    private void OnToggle(ToggleEvent e)
    {
        var open = string.Equals(e.NewState, "open", StringComparison.Ordinal);
        if (open == _open)
        {
            if (!open)
            {
                ForgetSearch();
            }

            return;
        }

        _open = open;
        _cursor = Unset;
        if (!open)
        {
            ForgetSearch();
        }
    }

    // What was searched for is forgotten when the browser says the list is shut, not here: the search field
    // still has focus now, and the rows a search hid would come back under the pointer while the list closes.
    private void Close()
    {
        _open = false;
        _cursor = Unset;
        _typing = false;
        if (IsCombobox)
        {
            _search = string.Empty;
        }
    }

    // Flux's `strict`: a closed combobox shows its answer, whatever was typed.
    private void ForgetSearch()
    {
        _search = string.Empty;
        _typing = false;
    }

    private void OnInputBlur()
    {
        // Only while closed. A click on a row blurs the input first, and unfiltering the list under the
        // pointer would move another row beneath the click.
        if (!_open)
        {
            ForgetSearch();
        }
    }

    // ----- The listbox's button ------------------------------------------------------------------------

    private async Task OnButtonKeyAsync(KeyboardEvent e, View view)
    {
        if (!_open)
        {
            await OnClosedButtonKeyAsync(e, view).ConfigureAwait(false);
            return;
        }

        if (!await OnListKeyAsync(e, view).ConfigureAwait(false) && TypedAhead(e, view) is { } hit and >= 0)
        {
            _cursor = hit;
        }
    }

    // Closed: an arrow opens, and a letter picks the next option that starts with it — a native select's way.
    private async Task OnClosedButtonKeyAsync(KeyboardEvent e, View view)
    {
        if (e.Key is Keys.ArrowDown or Keys.ArrowUp)
        {
            _open = true;
            _cursor = Unset;
            return;
        }

        if (TypedAhead(e, view) is { } hit and >= 0)
        {
            _cursor = hit;
            await PickAsync(hit, view, closes: false).ConfigureAwait(false);
        }
    }

    private int? TypedAhead(KeyboardEvent e, View view)
    {
        if (e.Key.Length != 1 || e.Key[0] == ' ' || e.CtrlKey || e.AltKey || e.MetaKey || HasSearchField)
        {
            return null;
        }

        var words = new string?[view.Parts.Rows.Count];
        for (var i = 0; i < words.Length; i++)
        {
            words[i] = view.Skips(i) ? null : view.Parts.Rows[i].Option.Text;
        }

        return _typeAhead.Next(e.Key, _open ? view.Cursor : IndexOfPicked(view), words, Clock);
    }

    private static int IndexOfPicked(View view) => view.PickedRows.FirstOrDefault()?.Index ?? -1;

    // ----- Keys every open list answers ----------------------------------------------------------------

    // True when the key was the list's.
    private async Task<bool> OnListKeyAsync(KeyboardEvent e, View view)
    {
        switch (e.Key)
        {
            case Keys.ArrowDown:
                _cursor = UiSelectNav.Step(view.Cursor, 1, view.Count, view.Skips);
                return true;
            case Keys.ArrowUp:
                _cursor = UiSelectNav.Step(view.Cursor, -1, view.Count, view.Skips);
                return true;
            case Keys.Enter:
                await PickAsync(view.Cursor, view).ConfigureAwait(false);
                return true;
            case Keys.Tab:
                Close();
                return true;
            default:
                return false;
        }
    }

    // ----- The listbox's search field ------------------------------------------------------------------

    private async Task SearchedAsync(string text, Callback<string> heard, bool opens)
    {
        _search = text;
        _typing = true;
        _cursor = First;
        if (opens)
        {
            _open = true;
        }

        if (!heard.HasValue)
        {
            return;
        }

        _busy = true;
        try
        {
            await heard.Invoke(text).ConfigureAwait(false);
        }
        finally
        {
            _busy = false;
        }
    }

    // ----- The combobox's input ------------------------------------------------------------------------

    private async Task OnInputKeyAsync(KeyboardEvent e, View view)
    {
        if (_open)
        {
            if (string.Equals(e.Key, Keys.Escape, StringComparison.Ordinal))
            {
                Close();
                await SayAsync(_input, PickedWords(view)).ConfigureAwait(false);
            }
            else
            {
                await OnListKeyAsync(e, view).ConfigureAwait(false);
            }

            return;
        }

        switch (e.Key)
        {
            case Keys.ArrowDown or Keys.ArrowUp:
                _open = true;
                _cursor = Unset;
                break;
            case Keys.Escape:
                // Flux's `clear="esc"`: the words go, the answer stays.
                _search = string.Empty;
                _typing = true;
                await SayAsync(_input, string.Empty).ConfigureAwait(false);
                break;
            case Keys.Tab:
                ForgetSearch();
                break;
            default:
                break;
        }
    }

    // ----- Picking -------------------------------------------------------------------------------------

    private async Task PickAsync(int index, View view, bool closes = true)
    {
        if (index < 0 || index >= view.Count || view.Skips(index))
        {
            return;
        }

        if (index == view.CreateIndex)
        {
            await CreateAsync(view).ConfigureAwait(false);
            return;
        }

        var value = view.Parts.Rows[index].Value;
        if (!IsMultiple)
        {
            await CommitAsync([value]).ConfigureAwait(false);
            if (closes)
            {
                Close();
            }

            if (IsCombobox)
            {
                await SayAsync(_input, view.Parts.Rows[index].Option.SelectedText).ConfigureAwait(false);
            }

            return;
        }

        // Several answers: the row is switched and the list stays open on it.
        _cursor = index;
        await CommitAsync(view.Picked.Contains(value) ? [.. view.Picked.Where(held => !Same(held, value))] : [.. view.Picked, value])
            .ConfigureAwait(false);
        if ((Clear ?? Ui.SelectClear.Select) == Ui.SelectClear.Select && view.Search.Length > 0)
        {
            await SearchedAsync(string.Empty, view.Parts.Search?.OnInput ?? default, opens: false).ConfigureAwait(false);
            await SayAsync(_searchInput, string.Empty).ConfigureAwait(false);
            _cursor = index;
        }
    }

    private static string PickedWords(View view) => view.PickedRows.FirstOrDefault()?.Option.SelectedText ?? string.Empty;

    private async Task CreateAsync(View view)
    {
        _busy = true;
        try
        {
            await view.Parts.Create!.OnClick.Invoke(view.Search).ConfigureAwait(false);
        }
        finally
        {
            _busy = false;
        }

        Close();
    }

    private async Task ClearAsync()
    {
        await CommitAsync([]).ConfigureAwait(false);
        ForgetSearch();
    }

    private static bool Same(T a, T b) => EqualityComparer<T>.Default.Equals(a, b);
}
