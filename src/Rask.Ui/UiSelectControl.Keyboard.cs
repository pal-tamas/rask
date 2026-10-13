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

    // The search field waits for a pause before the page hears it (UiSelectSearch.Debounce), as the last render
    // drew it.
    private bool _waits;

    // What the page last heard of the search. A pause that ends on the same text asks nothing again.
    private string _heard = string.Empty;

    private string ListId => HasSearchField ? Prefixed + "-list" : PanelId;

    private string SearchId => Prefixed + "-search";

    /// <summary>Whether the browser hides the rows a search leaves out; otherwise the page answers the search.</summary>
    private bool HidesRows => Searches && Filter != false;

    // ----- What is typed -------------------------------------------------------------------------------

    // The page's own search: every keystroke, or the text a pause ended on.
    private async Task HearAsync(string text, Callback<string> heard)
    {
        _search = text;
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

    // What a text field tells the page, by the attributes it carries. A search the page asked to hear is sent as
    // it asked — at every key, or at the pause the browser counts (`data-rask-on-pause`, rask-input.ts). One it
    // did not ask for is sent with the press that needs it — the create row's — and with nothing else: the field
    // waits for the next action, as a bound one does.
    private Dictionary<string, string?> HearingMarks(Callback<string> heard, int pause, bool creates)
    {
        var marks = new Dictionary<string, string?>(StringComparer.Ordinal);
        // With no page behind the render there is no handler to name, and nothing to wait for.
        var live = Rask.Core.Live.LiveRenderContext.CurrentSync;
        _waits = live is not null && pause > 0;
        if (live is null)
        {
            return marks;
        }

        if (_waits)
        {
            marks["data-rask-debounce"] = pause.ToString(System.Globalization.CultureInfo.InvariantCulture);
            marks["data-rask-on-pause"] = live.RegisterHandler((Func<string, Task>)(text => HearAsync(text, heard)));
        }
        else if (heard.HasValue)
        {
            marks["data-rask-on-input"] = live.RegisterHandler((Func<string, Task>)(text => HearAsync(text, heard)));
        }
        else if (creates)
        {
            marks["data-rask-on-change"] = live.RegisterHandler((Func<string, Task>)Carried);
            marks["data-rask-bind-on"] = "action";
        }

        return marks;
    }

    private Task Carried(string text)
    {
        _search = text;

        return Task.CompletedTask;
    }

    // ----- Picking -------------------------------------------------------------------------------------

    private async Task PickAsync(int index, View view)
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
            return;
        }

        // Several answers: the row is switched.
        await CommitAsync(view.Picked.Contains(value) ? [.. view.Picked.Where(held => !Same(held, value))] : [.. view.Picked, value])
            .ConfigureAwait(false);
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
            await view.Parts.Create!.OnClick.Invoke(view.Search).ConfigureAwait(false);
        }
        finally
        {
            _busy = false;
        }

        // The browser shuts the list and empties what was typed; the option is made of it, so it is forgotten here too.
        _search = string.Empty;
    }

    private Task ClearAsync() => CommitAsync([]);

    private static bool Same(T a, T b) => EqualityComparer<T>.Default.Equals(a, b);
}
