namespace Rask;

/// <summary>
///     A read face's rows (<see cref="IChoiceSource{TKey}" />, <c>Company.Choices(c =&gt; c.Name)</c>) as a paging
///     <c>.Choices(…)</c>: searched and paged in the database, with the picked rows named on the first page whether or not
///     it holds them, so the select or pillbox shows what it holds before anything else is found.
/// </summary>
/// <remarks>
///     <para>
///         One per select, kept from render to render through the step it made (<see cref="Of" />): a page builds its
///         source again every render, and a new one each time would read as a new search, asked again at once. The newest
///         source is the one asked; a page whose source narrows by another filter keys the select by that filter, so a
///         change starts the list over.
///     </para>
///     <para>
///         The first page of each answer is the picked rows it does not hold, then the page: its total counts them, and
///         the pages after it skip them, so the scrollbar is the whole list's and no row is asked twice. Every row seen is
///         remembered, and is the row a later render names its value by.
///     </para>
/// </remarks>
/// <typeparam name="TKey">The id's type.</typeparam>
internal sealed class UiSourceChoices<TKey>
{
    private readonly List<Ui.Choice<TKey>> _known = [];
    private IChoiceSource<TKey> _source;
    private Func<IReadOnlyCollection<TKey>> _picked;

    // How many picked rows the first page of the last answer put before its page, and for what was typed.
    private (string Typed, int Count) _outside = (string.Empty, 0);

    private UiSourceChoices(IChoiceSource<TKey> source, Func<IReadOnlyCollection<TKey>> picked)
    {
        (_source, _picked) = (source, picked);
        Find = Page;
    }

    /// <summary>The paging search the select asks: the same delegate every render, so it is not asked again for nothing.</summary>
    internal Func<string, Ui.ChoicePage, Task<IReadOnlyList<Ui.Choice<TKey>>>> Find { get; }

    /// <summary>The rows <paramref name="before" /> asked of, given this render's source; new for a select that asked none.</summary>
    /// <param name="before">What the last render's <c>.Choices(…)</c> made.</param>
    /// <param name="source">This render's source.</param>
    /// <param name="picked">The ids the select holds as it asks.</param>
    internal static UiSourceChoices<TKey> Of(UiChosen? before, IChoiceSource<TKey> source, Func<IReadOnlyCollection<TKey>> picked)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (UiChoices.PagedBy<TKey>(before)?.Target is UiSourceChoices<TKey> kept)
        {
            (kept._source, kept._picked) = (source, picked);
            return kept;
        }

        return new UiSourceChoices<TKey>(source, picked);
    }

    /// <summary>The picked rows already seen: what the select names its value by before the first answer.</summary>
    internal Ui.Choice<TKey>[] Held()
    {
        var picked = _picked();
        lock (_known)
        {
            return [.. _known.Where(row => picked.Contains(row.Value))];
        }
    }

    private async Task<IReadOnlyList<Ui.Choice<TKey>>> Page(string typed, Ui.ChoicePage page)
    {
        if (page.Skip == 0)
        {
            var first = await _source.Page(typed, 0, page.Take, _picked()).ConfigureAwait(false);
            Ui.Choice<TKey>[] rows = [.. first.Rows.Select(Choice)];
            Ui.Choice<TKey>[] outside = [.. first.Picked.Where(held => !rows.Any(row => Same(row.Value, held.Id))).Select(Choice)];
            Note(rows);
            Note(outside);
            _outside = (typed, outside.Length);
            return new Ui.ChoiceRows<TKey>([.. outside, .. rows], first.Total + outside.Length);
        }

        var before = string.Equals(_outside.Typed, typed, StringComparison.Ordinal) ? _outside.Count : 0;
        var next = await _source.Page(typed, Math.Max(0, page.Skip - before), page.Take).ConfigureAwait(false);
        Ui.Choice<TKey>[] more = [.. next.Rows.Select(Choice)];
        Note(more);
        return new Ui.ChoiceRows<TKey>(more, next.Total + before);
    }

    // Each row once, as it was seen last.
    private void Note(Ui.Choice<TKey>[] rows)
    {
        lock (_known)
        {
            foreach (var row in rows)
            {
                _known.RemoveAll(known => Same(known.Value, row.Value));
                _known.Add(row);
            }
        }
    }

    private static Ui.Choice<TKey> Choice(ChoiceRow<TKey> row) => new(row.Id, row.Text, row.Detail);

    private static bool Same(TKey a, TKey b) => EqualityComparer<TKey>.Default.Equals(a, b);
}
