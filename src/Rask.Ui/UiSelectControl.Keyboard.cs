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

    // The search field waits for a pause before the page hears it (UiSelectSearch.Debounce), as the last render
    // drew it. What the reader has typed since the page last heard is then the browser's alone: nothing here
    // knows it, so whatever empties the field says so to the field itself.
    private bool _waits;

    // What the page last heard of the search. A pause that ends on the same text asks nothing again.
    private string _heard = string.Empty;

#pragma warning disable S3459 // a mutable struct whose default is its empty state; Next() fills it in place
    private UiTypeAhead _typeAhead;
#pragma warning restore S3459

    // Where the pointer last was on the list. An open list sends `mouseenter` to the row that appears under a
    // pointer that has not moved; Flux's cursor follows the pointer only when it does.
    private (int X, int Y)? _pointer;

    // The pointer has picked in a list that stayed open. From then on a row that comes under it counts even
    // while it rests: a pill added to the trigger moves the list, and Flux's cursor moves with it.
    private bool _pointerActs;

    private readonly ElementRef<HTMLInputElement> _input = new();

    private readonly ElementRef<HTMLInputElement> _searchInput = new();

    private string ListId => HasSearchField ? Prefixed + "-list" : PanelId;

    private void Hovered(MouseEvent e, int index)
    {
        if (_pointerActs || _pointer != (e.ClientX, e.ClientY))
        {
            _pointer = (e.ClientX, e.ClientY);
            _cursor = index;
        }
    }

    private Task ClickedAsync(PointerEvent e, int index, View view)
    {
        _pointer = (e.ClientX, e.ClientY);
        _pointerActs = IsMultiple;

        return PickAsync(index, view);
    }

    // A click in the combobox opens it; with an answer showing, the answer is selected, ready to type over.
    private async Task OnInputClickAsync(View view, bool focuses = false)
    {
        _open = true;
        if (focuses)
        {
            await UiInputReach.ReachAsync(() => _input.Focus()).ConfigureAwait(false);
        }

        if (view.Picked.Count > 0 && !_typing)
        {
            await UiInputReach.ReachAsync(() => _input.Select()).ConfigureAwait(false);
        }
    }

    // The reader opened or closed the popover: a click on the button, Escape, a click elsewhere. What the runtime
    // does to it because C# said so is not reported back, so this is news every time.
    private void OnToggle(ToggleEvent e)
    {
        var open = string.Equals(e.NewState, "open", StringComparison.Ordinal);
        if (!open && _open)
        {
            // The browser shut it: a click elsewhere, which may be the click on the trigger that comes next.
            _dismissed = Clock.GetTimestamp();
        }

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
        _pointerActs = false;
        if (!open)
        {
            ForgetSearch();
        }
    }

    // The list's own state. What its search field holds is ShutAsync's to forget, or the toggle's.
    private void Close()
    {
        _open = false;
        _pointerActs = false;
        _cursor = Unset;
        _typing = false;
        if (IsCombobox)
        {
            _search = string.Empty;
        }
    }

    // C# shuts the list. The runtime carries that out and raises no toggle for it — the page already knows —
    // so what the search field holds is forgotten here, in the render that shuts the list, and said to the
    // field itself: it has the focus, and a render does not write over a field being typed into.
    private async Task ShutAsync()
    {
        Close();
        if (HasSearchField && (_search.Length > 0 || _waits))
        {
            _search = string.Empty;
            await UiInputReach.SayAsync(_searchInput, string.Empty).ConfigureAwait(false);
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
        // A button is pressed by Space by itself; the pillbox's trigger is no button, and Flux opens it on Space.
        if (e.Key is Keys.ArrowDown or Keys.ArrowUp || (Pills is not null && string.Equals(e.Key, " ", StringComparison.Ordinal)))
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
                await ShutAsync().ConfigureAwait(false);
                return true;
            default:
                return false;
        }
    }

    // ----- The listbox's search field ------------------------------------------------------------------

    private Task SearchedAsync(string text, Callback<string> heard, bool opens)
    {
        Typed(text, opens);

        return HearAsync(text, heard);
    }

    // What the list itself makes of the text: the rows it leaves, and the cursor back at the top.
    private void Typed(string text, bool opens)
    {
        _search = text;
        _typing = true;
        _cursor = First;
        if (opens)
        {
            _open = true;
        }
    }

    // The page's own search: every keystroke, or the text a pause ended on.
    private async Task HearAsync(string text, Callback<string> heard)
    {
        if (!heard.HasValue || (_waits && string.Equals(text, _heard, StringComparison.Ordinal)))
        {
            return;
        }

        _heard = text;
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

    // The pause the browser counted has ended (`data-rask-on-pause`). A list that has shut meanwhile asks
    // nothing: the reader picked, or left, and the text went with the list.
    private Task PausedAsync(string text, Callback<string> heard)
    {
        if (!_open)
        {
            return Task.CompletedTask;
        }

        return Filter == false ? SearchedAsync(text, heard, opens: false) : HearAsync(text, heard);
    }

    // The search field's own clear button.
    private async Task ClearSearchAsync(Callback<string> heard)
    {
        await SearchedAsync(string.Empty, heard, opens: false).ConfigureAwait(false);
        await ForgetUnheardAsync().ConfigureAwait(false);
    }

    // Empties a field that may hold text no render has drawn: a render changes nothing in it then.
    private async Task ForgetUnheardAsync()
    {
        if (_waits)
        {
            await UiInputReach.SayAsync(_searchInput, string.Empty).ConfigureAwait(false);
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
                await UiInputReach.SayAsync(_input, PickedWords(view)).ConfigureAwait(false);
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
                await UiInputReach.SayAsync(_input, string.Empty).ConfigureAwait(false);
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
                await ShutAsync().ConfigureAwait(false);
            }

            if (IsCombobox)
            {
                await UiInputReach.SayAsync(_input, view.Parts.Rows[index].Option.SelectedText).ConfigureAwait(false);
            }

            return;
        }

        // Several answers: the row is switched and the list stays open on it.
        _cursor = index;
        await CommitAsync(view.Picked.Contains(value) ? [.. view.Picked.Where(held => !Same(held, value))] : [.. view.Picked, value])
            .ConfigureAwait(false);
        if ((Clear ?? Ui.SelectClear.Select) == Ui.SelectClear.Select && (view.Search.Length > 0 || _waits))
        {
            await SearchedAsync(string.Empty, SearchHeard(view), opens: false).ConfigureAwait(false);
            await UiInputReach.SayAsync(PillsCombobox ? _input : _searchInput, string.Empty).ConfigureAwait(false);
            // The select's cursor stays on the row just switched; the pillbox's goes back to the top, as Flux's does.
            _cursor = Pills is null ? index : First;
        }
    }

    // Who hears what is typed: the search field's slot, or the pillbox's own input.
    private Callback<string> SearchHeard(View view) =>
        (PillsCombobox ? view.Parts.Input?.OnInput : view.Parts.Search?.OnInput) ?? default;

    // The combobox's words for its answer: the first picked row's, or the answer's as last drawn once a search
    // the page answers has taken its row away.
    private static string PickedWords(View view) =>
        view.PickedRows.FirstOrDefault()?.Option.SelectedText ?? view.Answer?.SelectedText ?? string.Empty;

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

        await ShutAsync().ConfigureAwait(false);
        if (PillsCombobox)
        {
            // The option is made of what was typed, so the pillbox's own input starts over.
            await ForgetTypedAsync(view).ConfigureAwait(false);
        }
    }

    private async Task ClearAsync()
    {
        await CommitAsync([]).ConfigureAwait(false);
        ForgetSearch();
    }

    private static bool Same(T a, T b) => EqualityComparer<T>.Default.Equals(a, b);

    // ----- What only draws (UiDraws) -------------------------------------------------------------------

    /// <summary>Whether a new answer is held for the next action: nothing of the page's hears it now.</summary>
    private bool CommitDraws => UiDraws.Waits(this, Bound is not null);

    /// <summary>
    ///     Whether picking a row only draws. Not where several are picked over a search the page hears: that
    ///     pick empties the search, and the page is told.
    /// </summary>
    private bool PickDraws(View view) => CommitDraws && !(IsMultiple && SearchHeard(view).HasValue);

    /// <summary>Whether no key can reach the page: Enter picks, and on a "create" row it runs the page's code.</summary>
    private bool KeysDraw(View view) => PickDraws(view) && view.Parts.Create is null;
}
