using Rask.Core;

namespace Rask;

// What the keyboard and the pointer do, as recorded on Flux's page: an arrow or a click opens the list, typing
// opens and filters it, Enter writes the active item into the input, Escape closes and empties the input, and
// Tab leaves with whatever was typed. The arrows stop at either end; Home and End are the text's own.
public sealed partial class UiAutocomplete
{
    private const int Unset = -1;

    private const int First = -2;

    private readonly ElementRef<HTMLInputElement> _input = new();

    private bool _open;

    // The row the keyboard is on: an index into the items, Unset to start from the picked item, First to
    // start from the top — where typing puts it.
    private int _cursor = Unset;

    // What is typed since the list last showed everything: what it is filtered by.
    private string _search = string.Empty;

    // The input says what is being typed rather than what the page holds.
    private bool _typing;

    // The item last picked from the list. Flux keeps it marked as selected whatever is typed afterwards.
    private string? _picked;

    // Where the pointer last was on the list: a row that appears under a pointer that has not moved is not hovered.
    private (int X, int Y)? _pointer;

    /// <summary>One item, as written: where it sits among all of them, and the text picking it writes.</summary>
    private sealed record Item(int Index, UiAutocompleteItem Part, string Text);

    /// <summary>One render's reading of the list: what the page holds, which items the text leaves, where the cursor is.</summary>
    private sealed class View(string text, List<Item> items, bool[] shown)
    {
        internal string Text { get; } = text;

        internal List<Item> Items { get; } = items;

        internal bool[] Shown { get; } = shown;

        internal int Cursor { get; set; } = Unset;

        /// <summary>Whether the cursor skips row <paramref name="index" />: filtered out or disabled.</summary>
        internal bool Skips(int index) => !Shown[index] || Items[index].Part.Disabled == true;
    }

    private View Read()
    {
        var text = UiFormCommit.Resolve(this).Current ?? string.Empty;
        List<Item> items = [];
        foreach (var part in (Children ?? []).OfType<UiAutocompleteItem>())
        {
            items.Add(new Item(items.Count, part, UiSelectText.Of(part.Children)));
        }

        var filters = _typing && _search.Length > 0;
        var view = new View(text, items, [.. items.Select(item => !filters || UiSelectText.Contains(item.Text, _search))]);
        view.Cursor = Place(view);

        return view;
    }

    // Where the cursor is for this render. A row it can no longer be on sends it back to the start.
    private int Place(View view)
    {
        if (_cursor >= 0 && _cursor < view.Items.Count && !view.Skips(_cursor))
        {
            return _cursor;
        }

        var picked = _cursor == First
            ? null
            : view.Items.Find(item => string.Equals(item.Text, _picked, StringComparison.Ordinal) && !view.Skips(item.Index));

        return picked?.Index ?? UiSelectNav.FirstEnabled(view.Items.Count, view.Skips);
    }

    private void Typed(string text)
    {
        _search = text;
        _typing = true;
        _open = true;
        _cursor = First;
    }

    private void Opened()
    {
        if (Disabled != true && ReadOnly != true)
        {
            _open = true;
        }
    }

    // The browser closed the popover: Escape, a click elsewhere. It never opens one — only a render does, and
    // the toggle that render causes arrives after it, when the list may be shut again. Taken as news, that
    // late "open" opened the list, whose own toggle shut it, and so on for as long as the page was up.
    private void OnToggle(ToggleEvent e)
    {
        if (string.Equals(e.NewState, "open", StringComparison.Ordinal))
        {
            return;
        }

        if (_open)
        {
            _open = false;
            _cursor = Unset;
        }

        _typing = false;
    }

    private void Close()
    {
        _open = false;
        _cursor = Unset;
        _typing = false;
        _search = string.Empty;
    }

    private void Hovered(MouseEvent e, int index)
    {
        if (_pointer != (e.ClientX, e.ClientY))
        {
            _pointer = (e.ClientX, e.ClientY);
            _cursor = index;
        }
    }

    private async Task ClickedAsync(PointerEvent e, int index, View view)
    {
        _pointer = (e.ClientX, e.ClientY);
        await PickAsync(index, view).ConfigureAwait(false);
        // The click took focus to the list; Flux leaves it in the input, ready to type on.
        await UiInputReach.ReachAsync(() => _input.Focus()).ConfigureAwait(false);
    }

    private async Task OnKeyAsync(KeyboardEvent e, View view)
    {
        switch (e.Key)
        {
            case Keys.ArrowDown or Keys.ArrowUp when !_open:
                Opened();
                _cursor = Unset;
                break;
            case Keys.ArrowDown:
                _cursor = UiSelectNav.Step(view.Cursor, 1, view.Items.Count, view.Skips);
                break;
            case Keys.ArrowUp:
                _cursor = UiSelectNav.Step(view.Cursor, -1, view.Items.Count, view.Skips);
                break;
            case Keys.Enter when _open:
                await PickAsync(view.Cursor, view).ConfigureAwait(false);
                break;
            case Keys.Escape:
                // Flux's `clear="esc"`: the list shuts and the input is emptied, open or not. The browser
                // emptied it at the key (UiInputHost.ClearKeys); what is typed since is the next text.
                Close();
                await CommitAsync(string.Empty).ConfigureAwait(false);
                break;
            case Keys.Tab:
                await LeaveAsync().ConfigureAwait(false);
                break;
            default:
                break;
        }
    }

    // Tab leaves with what was typed, and the page holds it from this render on: the input's own `change`
    // comes a round trip behind the key, and the field showed the text before it in between.
    private async Task LeaveAsync()
    {
        var typed = _typing ? _search : null;
        Close();
        if (typed is not null)
        {
            await CommitAsync(typed).ConfigureAwait(false);
        }
    }

    private async Task PickAsync(int index, View view)
    {
        if (index < 0 || index >= view.Items.Count || view.Skips(index))
        {
            return;
        }

        var text = view.Items[index].Text;
        _picked = text;
        Close();
        await UiInputReach.SayThenTypeOnAsync(_input, text).ConfigureAwait(false);
        await CommitAsync(text).ConfigureAwait(false);
    }

    // The text reaches the page when it is picked, cleared, or typed and left — the input's own `change`.
    // A pick is followed by that `change` when the input is left; the page hears a text once.
    private async Task CommitAsync(string text)
    {
        var (accessor, context, current) = UiFormCommit.Resolve(this);
        if (!string.Equals(current ?? string.Empty, text, StringComparison.Ordinal))
        {
            await UiFormCommit.CommitAsync(this, accessor, context, text).ConfigureAwait(false);
        }
    }
}
