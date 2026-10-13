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

    // Who hears what is typed — the search field's slot, or the input's — as the last render read it.
    private Callback<string> _hears;

    // How long the field typed into waits before the page hears it (its slot's Debounce), as the last render
    // read it. While it waits, what the reader has typed since the page last heard is the browser's alone:
    // nothing here knows it, so whatever rewrites the field says so to the field itself.
    private int _pause;

    // What the page last heard of the search. A pause that ends on the same text asks nothing again, and a
    // field the control empties tells the page so only when the page thinks otherwise.
    private string _heard = string.Empty;

    // The page has heard something typed since the field last came to rest.
    private bool _searched;

    // What a closed combobox says: its answer's words, as the last render read them.
    private string _answer = string.Empty;

    private bool Waits => _pause > 0;

    // The field that is typed into: the combobox's input or the pillbox's, else the listbox's search field.
    private ElementRef<HTMLInputElement> TypedInto => IsCombobox || PillsCombobox ? _input : _searchInput;

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

    // The list has shut and its field says what a closed one says: nothing, or a combobox's answer. The page's
    // search follows the field, as Flux's does — measured on its backend-search examples, `search` is written
    // "" on Escape and on a click elsewhere, and the answer's words when a combobox goes back to saying them.
    // A field that waits may hold text no render drew, and a render changes nothing in it then.
    private Task SettleAsync(string? says = null, bool held = false)
    {
        var text = says ?? (IsCombobox ? _answer : string.Empty);
        if (Waits || held)
        {
            UiInputReach.Tell(TypedInto, text);
        }

        return FollowAsync(text, anew: says is not null);
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
    private async Task ShutAsync(string? says = null)
    {
        Close();
        if (PillsCombobox)
        {
            // What its input holds is ForgetTypedAsync's to empty.
            return;
        }

        var held = HasSearchField && _search.Length > 0;
        if (held)
        {
            _search = string.Empty;
        }

        await SettleAsync(says, held).ConfigureAwait(false);
    }

    // Flux's `strict`: a closed combobox shows its answer, whatever was typed.
    private void ForgetSearch()
    {
        _search = string.Empty;
        _typing = false;
    }

    private async Task OnInputBlurAsync()
    {
        // Only while closed. A click on a row blurs the input first, and unfiltering the list under the
        // pointer would move another row beneath the click.
        if (!_open)
        {
            ForgetSearch();
            await SettleAsync().ConfigureAwait(false);
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

    private Task SearchedAsync(string text, bool opens)
    {
        Typed(text, opens);

        return HearAsync(text);
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
    private async Task HearAsync(string text)
    {
        if (!_hears.HasValue || (Waits && string.Equals(text, _heard, StringComparison.Ordinal)))
        {
            return;
        }

        _heard = text;
        _searched = true;
        _busy = true;
        try
        {
            await _hears.Invoke(text).ConfigureAwait(false);
        }
        finally
        {
            _busy = false;
        }
    }

    // The control itself has rewritten its field, and the page hears of it when it thinks otherwise. A field
    // that only came back to rest tells a page that heard a search since it last rested; one that says something
    // new (a combobox's pick, its answer cleared) tells it whatever went before, as Flux's input does. A
    // combobox loaded with an answer and opened and shut tells nothing: nothing was typed.
    private async Task FollowAsync(string text, bool anew = false)
    {
        var tells = (anew || _searched) && !string.Equals(text, _heard, StringComparison.Ordinal);
        if (tells)
        {
            await HearAsync(text).ConfigureAwait(false);
        }

        _searched = false;
    }

    // The pause the browser counted has ended (`data-rask-on-pause`). A list that has shut meanwhile asks
    // nothing: the reader picked, or left, and the text went with the list.
    private Task PausedAsync(string text)
    {
        if (!_open)
        {
            return Task.CompletedTask;
        }

        if (Filter == false && !string.Equals(text, _search, StringComparison.Ordinal))
        {
            Typed(text, opens: false);
        }

        return HearAsync(text);
    }

    // What a keystroke is to the control. With no pause, the list's own reading of the text and the page's
    // search. With one, the list's alone, and only where there is something for it to do: rows the control
    // hides itself, or a closed list the first key opens. A list the page filters has nothing to draw until
    // the page has answered, and hears nothing until the pause.
    private Func<string, Task>? AtEveryKey(bool opens)
    {
        if (!Waits)
        {
            return text => SearchedAsync(text, opens);
        }

        return Filter != false || (opens && !_open) ? text => TypedAsync(text, opens) : null;
    }

    private Task TypedAsync(string text, bool opens)
    {
        Typed(text, opens);

        return Task.CompletedTask;
    }

    // The search field's own clear button.
    private async Task ClearSearchAsync()
    {
        await SearchedAsync(string.Empty, opens: false).ConfigureAwait(false);
        if (Waits)
        {
            await UiInputReach.SayAsync(_searchInput, string.Empty).ConfigureAwait(false);
        }
    }

    // What the field holds now, asked of the browser where a field that waits may hold more than the page
    // has heard. The create row is about that text: Flux sends it with the press (measured: typed and pressed
    // at once, the request carries `search` and the call together).
    private async Task<string> TypedNowAsync(View view)
    {
        if (!Waits)
        {
            return view.Search;
        }

        try
        {
            return await TypedInto.Value.ConfigureAwait(false) ?? view.Search;
        }
        catch (InvalidOperationException)
        {
            // No browser behind this render: what the page last heard is all there is.
            return view.Search;
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
                await FollowAsync(PickedWords(view)).ConfigureAwait(false);
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
                await FollowAsync(string.Empty, anew: true).ConfigureAwait(false);
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
            var words = view.Parts.Rows[index].Option.SelectedText;
            if (closes)
            {
                // A combobox goes on to say its new answer, and the page's search with it.
                await ShutAsync(IsCombobox ? words : null).ConfigureAwait(false);
            }

            if (IsCombobox)
            {
                await UiInputReach.SayAsync(_input, words).ConfigureAwait(false);
            }

            return;
        }

        // Several answers: the row is switched and the list stays open on it.
        _cursor = index;
        await CommitAsync(view.Picked.Contains(value) ? [.. view.Picked.Where(held => !Same(held, value))] : [.. view.Picked, value])
            .ConfigureAwait(false);
        if (await ForgetAtChoiceAsync(view).ConfigureAwait(false))
        {
            // The select's cursor stays on the row just switched; the pillbox's goes back to the top, as Flux's does.
            _cursor = Pills is null ? index : First;
        }
    }

    // Flux's `clear="select"`: a choice among several empties what was typed. True when there was anything to empty.
    private async Task<bool> ForgetAtChoiceAsync(View view)
    {
        if ((Clear ?? Ui.SelectClear.Select) != Ui.SelectClear.Select || (view.Search.Length == 0 && !Waits))
        {
            return false;
        }

        await SearchedAsync(string.Empty, opens: false).ConfigureAwait(false);
        await UiInputReach.SayAsync(TypedInto, string.Empty).ConfigureAwait(false);

        return true;
    }

    // Who hears what is typed, and after how long a pause: the input's slot where one is typed into, else the
    // search field's.
    private void ReadSearch(Parts parts)
    {
        var typed = IsCombobox || PillsCombobox;
        _hears = (typed ? parts.Input?.OnInput : parts.Search?.OnInput) ?? default;
        _pause = UiSearchPause.Of(_hears, typed ? parts.Input?.Debounce : parts.Search?.Debounce);
    }

    // The combobox's words for its answer: the first picked row's, or the answer's as last drawn once a search
    // the page answers has taken its row away.
    private static string PickedWords(View view) =>
        view.PickedRows.FirstOrDefault()?.Option.SelectedText ?? view.Answer?.SelectedText ?? string.Empty;

    private async Task CreateAsync(View view)
    {
        _busy = true;
        try
        {
            await view.Parts.Create!.OnClick.Invoke(await TypedNowAsync(view).ConfigureAwait(false)).ConfigureAwait(false);
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
        if (IsCombobox)
        {
            // With no answer left to say, the input is empty, and the page's search with it.
            await FollowAsync(string.Empty, anew: true).ConfigureAwait(false);
        }
    }

    private static bool Same(T a, T b) => EqualityComparer<T>.Default.Equals(a, b);
}
