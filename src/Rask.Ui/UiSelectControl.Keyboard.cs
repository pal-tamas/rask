using Rask.Core.Live;

namespace Rask;

// What the page hears of a list. Where the reader is in it — the lit row, whether it is open, the rows a search
// leaves — is the browser's (`data-rask-list`, rask-list.ts), as it is in Flux, and sends nothing. The page
// hears a row PRESSED, by the pointer or by Enter, and what is typed where it asked to.
public abstract partial class UiSelectControl<T>
{
    // What the page knows was typed: heard by its own search, or carried by the press that needs it.
    private string _search = string.Empty;

    // A handler of the page's is running: a search it answers, or the option it is creating.
    private bool _busy;

    // Who hears what is typed — the search field's slot, or the input's — as the last render read it.
    private Callback<string> _hears;

    // How long the field typed into waits before the page hears it (its slot's Debounce), as the last render
    // read it. The browser counts it (`data-rask-on-pause`, rask-input.ts).
    private int _pause;

    // What the page last heard of the search. A pause that ends on the same text asks nothing again.
    private string _heard = string.Empty;

    private string ListId => HasSearchField ? Prefixed + "-list" : PanelId;

    private string SearchId => Prefixed + "-search";

    /// <summary>Whether the browser hides the rows a search leaves out; otherwise the page answers the search.</summary>
    private bool HidesRows => Searches && Filter != false;

    // ----- What is typed -------------------------------------------------------------------------------

    // Who hears what is typed, and after how long a pause: the input's slot where one is typed into, else the
    // search field's.
    private void ReadSearch(Parts parts)
    {
        var typed = IsCombobox || PillsCombobox;
        _hears = (typed ? parts.Input?.OnInput : parts.Search?.OnInput) ?? default;
        _pause = UiSearchPause.Of(_hears, typed ? parts.Input?.Debounce : parts.Search?.Debounce);
    }

    // The page's own search: every keystroke, the text a pause ended on, or what the browser wrote into the
    // field itself once a search the page had heard was over (rask-list.ts) — nothing when the list shut, a
    // combobox's answer when it went back to saying it.
    private async Task HearAsync(string text)
    {
        _search = text;
        if (!_hears.HasValue || (_pause > 0 && string.Equals(text, _heard, StringComparison.Ordinal)))
        {
            return;
        }

        _heard = text;
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

    // What a text field tells the page, by the attributes it carries. A search the page asked to hear is sent as
    // it asked — at every key, or at the pause the browser counts (`data-rask-on-pause`, rask-input.ts) — and
    // nothing else sends it: a row pressed during the pause is a row of the list as drawn. A field with a row
    // that makes an option of its text also hands that text over with the press that needs it, and with
    // nothing else: it waits for the next action, as a bound one does.
    private Dictionary<string, string?> HearingMarks(Dictionary<string, string?> marks, bool creates)
    {
        // With no page behind the render there is no handler to name, and nothing to wait for.
        if (LiveRenderContext.CurrentSync is not { } live)
        {
            return marks;
        }

        var hearing = UiSearchPause.Over(marks, _pause, HearAsync);
        if (_pause == 0 && _hears.HasValue)
        {
            hearing = new Dictionary<string, string?>(marks, StringComparer.Ordinal)
            {
                ["data-rask-on-input"] = live.RegisterHandler((Func<string, Task>)HearAsync),
            };
        }
        else if (creates)
        {
            hearing = new Dictionary<string, string?>(hearing, StringComparer.Ordinal)
            {
                ["data-rask-on-change"] = live.RegisterHandler((Func<string, Task>)Carried),
                ["data-rask-bind-on"] = "action",
            };
        }

        return hearing;
    }

    private Task Carried(string text)
    {
        _search = text;

        return Task.CompletedTask;
    }

    // ----- Picking -------------------------------------------------------------------------------------

    // The row pressed is the one that held `value` when the press was read. A press that outlived its page finds
    // this handler by that value (Component.StaleEvents), so the row at `index` now is the one the reader saw.
    private Task PickedAsync(int index, T value)
    {
        var view = Latest;
        if (index >= view.Parts.Rows.Count || !Same(view.Parts.Rows[index].Value, value) || view.Skips(index))
        {
            return Task.CompletedTask;
        }

        if (!IsMultiple)
        {
            return CommitAsync([value]);
        }

        // Several answers: the row is switched.
        return CommitAsync(view.Picked.Contains(value) ? [.. view.Picked.Where(held => !Same(held, value))] : [.. view.Picked, value]);
    }

    // The combobox's words for its answer: the first picked row's, or the answer's as last drawn once a search
    // the page answers has taken its row away.
    private static string PickedWords(View view) =>
        view.PickedRows.FirstOrDefault()?.Option.SelectedText ?? view.Answer?.SelectedText ?? string.Empty;

    // The create row is about what the field holds: heard at every key, or carried ahead of this press.
    private async Task CreateAsync()
    {
        var view = Latest;
        if (view.Parts.Create is not { } create || view.Skips(view.CreateIndex))
        {
            return;
        }

        _busy = true;
        try
        {
            await create.OnClick.Invoke(SearchText(view.Parts)).ConfigureAwait(false);
        }
        finally
        {
            _busy = false;
        }

        // The browser shuts the list and empties what was typed; the option is made of it, so it is forgotten here too.
        _search = string.Empty;
    }

    private Task ClearAsync() => CommitAsync([]);

    private Task RemoveAsync(T value) =>
        CommitAsync([.. Latest.Picked.Where(held => !Same(held, value))]);

    private static bool Same(T a, T b) => EqualityComparer<T>.Default.Equals(a, b);

    // ----- What only draws (UiDraws) -------------------------------------------------------------------

    /// <summary>Whether a new answer is held for the next action: nothing of the page's hears it now.</summary>
    private bool CommitDraws => UiDraws.Waits(this, Bound is not null);
}
