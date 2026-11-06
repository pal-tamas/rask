namespace Rask;

// A search the page answers through `Search` (#1284): the select asks, holds the answer, and draws it.
//
// Flux's backend search is the page's own state — `:filter="false"`, a `$search` the search field writes with
// `wire:model.live.debounce`, and options rendered from a query over it — and both apps wrote that state out as a
// component of their own. Here the select holds it. It asks for "" when it mounts, so the list opens on the first
// few; then for what was typed, once typing pauses. The answer replaces the rows the page wrote as children, which
// are the rows until the first answer is here — the one option the value holds, so the select can name it before
// anything is found. An answer to a question asked before the last one is dropped.
//
// A picked option is never lost to a search that does not bring it: it stays in the list, first, in the order it
// was picked, so it can be seen and taken off. A row is kept for as long as the value holds it, and no longer.
public abstract partial class UiSelectControl<T>
{
    // The rows the last answer brought; null until one has come.
    private Component?[]? _found;

    // The picked options, each as a row last drew it: what the list goes on offering when an answer leaves one out.
    private (T Value, UiSelectOption Option)[] _remembered = [];

    // How many rows of this render are picked ones the answer did not bring.
    private int _keptCount;

    // Which question was asked last.
    private int _asked;

    /// <summary>Whether the page answers what is typed through <see cref="Search" />.</summary>
    private bool Asks => Asked is not null;

    /// <inheritdoc />
    // Before anything is typed the list offers what an empty search finds. A server page waits for it, so the first
    // HTML already carries the rows.
    protected override Task OnMount() => Asks ? AnswerAsync(string.Empty) : Task.CompletedTask;

    // Asks the page, and keeps the answer if no later question has been asked meanwhile. What is typed is trimmed: a
    // space typed after a word asks nothing new.
    private async Task AnswerAsync(string typed)
    {
        if (Asked is not { } search)
        {
            return;
        }

        var asked = ++_asked;
        var answer = search.Invoke(typed.Trim()) is { } pending ? await pending.ConfigureAwait(false) : null;
        if (asked != _asked)
        {
            return;
        }

        // A pillbox's options are its own parts: the select reads them as its options.
        Func<Component?, Component?> read = Pills is null ? static row => row : UiPillboxParts.Translate;
        _found = [.. (answer ?? []).Select(read)];
    }

    // The children the select draws: what the page wrote, with its options and groups replaced by the answer — the
    // picked options it left out first — where the first of them was written, or before a create row that came first.
    private IEnumerable<Component?>? Offered(IReadOnlyList<T> picked)
    {
        _keptCount = 0;
        if (!Asks)
        {
            return Written;
        }

        if (_found is not { } found)
        {
            RememberPicked(picked, Parts.Read(Written).Rows);
            return Written;
        }

        var foundRows = Parts.Read(found).Rows;
        RememberPicked(picked, [.. foundRows, .. Parts.Read(Written).Rows]);
        var kept = _remembered
            .Where(held => !foundRows.Exists(row => Same(row.Value, held.Value)))
            .Select(held => (Component?)held.Option)
            .ToArray();
        _keptCount = kept.Length;

        return Replaced(Written ?? [], [.. kept, .. found]);
    }

    private static IEnumerable<Component?> Replaced(IEnumerable<Component?> children, Component?[] rows)
    {
        var placed = false;
        foreach (var child in children)
        {
            var isRow = child is UiSelectOption or UiSelectGroup;
            if (!placed && (isRow || child is UiSelectOptionCreate))
            {
                placed = true;
                foreach (var row in rows)
                {
                    yield return row;
                }
            }

            if (!isRow)
            {
                yield return child;
            }
        }

        if (!placed)
        {
            foreach (var row in rows)
            {
                yield return row;
            }
        }
    }

    // Each picked value's option as this render offers it, or as a render before last drew it.
    private void RememberPicked(IReadOnlyList<T> picked, List<Row> rows)
    {
        var remembered = new List<(T, UiSelectOption)>(picked.Count);
        foreach (var value in picked)
        {
            if (rows.Find(row => Same(row.Value, value)) is { } row)
            {
                remembered.Add((value, row.Option));
            }
            else if (Array.FindIndex(_remembered, held => Same(held.Value, value)) is >= 0 and var at)
            {
                remembered.Add(_remembered[at]);
            }
        }

        _remembered = [.. remembered];
    }

    // How many rows answer the search: the picked ones it left out do not.
    private int Found(Parts parts) => parts.Rows.Count - _keptCount;
}
