namespace Rask;

// `.Choices(…)`: several rows of another table, picked out of a list the pillbox draws from rows rather than options.
public sealed partial class UiPillbox<T>
{
    // What the last `.Choices(…)` said, and the step that said it; null for a pillbox of options written as children.
    private UiChoosing? _choices;

    private UiChosen? Choosing => _choices?.Chosen;

    /// <summary>
    ///     Offers <paramref name="choices" />: a search field over them, in the reader's alphabet, and the catalog's
    ///     "Choose..." while nothing is picked.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Not Flux's: an addition the owner approved (2026-11-06) for a picker of rows. What it fills in is only
    ///         a default — <c>.Placeholder(…)</c>, <c>.Searchable(false)</c> or <c>.Combobox</c> say otherwise, and
    ///         options written as children come after the rows. A row's <see cref="Ui.Choice{T}.Detail" /> is
    ///         a muted word after its words in the list (Flux's pillbox option has no line under them); its pill says
    ///         the words alone.
    ///     </para>
    ///     <code>
    ///     Ui.Pillbox.Bind(() =&gt; model.VehicleIds).Choices(vehicles)
    ///     </code>
    /// </remarks>
    /// <param name="choices">The rows, in any order: <c>new Ui.Choice&lt;int&gt;(v.Id, v.Plate)</c>.</param>
    /// <returns>This pillbox.</returns>
    public UiPillbox<T> Choices(IEnumerable<Ui.Choice<T>> choices)
    {
        // The rows are read once, here: a Key written after this step replays it onto the instance the key keeps.
        ArgumentNullException.ThrowIfNull(choices);
        Ui.Choice<T>[] rows = [.. choices];
        Choose((before, leading) => (UiChoices.Held(rows, UiChoices.Lead<T>(leading), mayBeEmpty: false, before, out var changed, pills: true), changed));
        return this;
    }

    /// <summary>
    ///     Offers the rows <paramref name="find" /> answers for what is typed — the best few from the database, for a
    ///     list too long to send whole — as <see cref="Search" /> does, in the reader's alphabet.
    /// </summary>
    /// <remarks>
    ///     As <see cref="Choices(IEnumerable{Ui.Choice{T}})" />, and as <c>Search</c>: asked for <c>""</c> when the
    ///     pillbox mounts and once typing pauses, and every picked row stays in the list while a search does not bring it.
    ///     <code>
    ///     Ui.Pillbox.Bind(() =&gt; model.ProductIds).Choices(Product.Named, held: products)
    ///     </code>
    /// </remarks>
    /// <param name="find">The rows for what was typed, trimmed: the first few for nothing typed.</param>
    /// <param name="held">The rows picked when the page was drawn, so their pills say their words before anything is found.</param>
    /// <returns>This pillbox.</returns>
    public UiPillbox<T> Choices(Func<string, Task<IReadOnlyList<Ui.Choice<T>>>> find, IEnumerable<Ui.Choice<T>>? held = null)
    {
        Ui.Choice<T>[] shown = [.. held ?? []];
        Choose((before, leading) => (UiChoices.Asked(find, shown, UiChoices.Lead<T>(leading), mayBeEmpty: false, before, out var changed, pills: true), changed));
        return this;
    }

    /// <summary>
    ///     Offers the rows <paramref name="find" /> answers for what is typed a page at a time: the first page as the list
    ///     opens and once typing pauses, and the next one as the reader scrolls near the end of the rows already here — for
    ///     a list too long to send whole that is still scrolled through to its last row.
    /// </summary>
    /// <remarks>
    ///     As <see cref="Choices(Func{string, Task{IReadOnlyList{Ui.Choice{T}}}}, IEnumerable{Ui.Choice{T}}?)" />, but
    ///     asked for <see cref="Ui.ChoicePage" />s: an answer of fewer rows than <see cref="Ui.ChoicePage.Take" /> is the
    ///     end of the list. A page answered as <see cref="Ui.ChoiceRows{T}" /> says how many rows there are in all: the list
    ///     is that long from its first page, its scrollbar the whole list's, and the rows a scroll reaches are asked for at
    ///     once. The rows are drawn in the order <paramref name="find" /> gives them, page after page — order
    ///     the query (<c>OrderBy</c>) so the pages follow on. Typing starts again from the first page, a page asked for
    ///     older words is dropped, and every picked row stays in the list.
    ///     <code>
    ///     Ui.Pillbox.Bind(() =&gt; model.ProductIds).Choices((typed, page) =&gt; Product.Named(typed, page), held: products)
    ///     </code>
    /// </remarks>
    /// <param name="find">The page of rows for what was typed, trimmed: <c>query.Skip(page.Skip).Take(page.Take)</c>.</param>
    /// <param name="held">The rows picked when the page was drawn, so their pills say their words before anything is found.</param>
    /// <returns>This pillbox.</returns>
    public UiPillbox<T> Choices(Func<string, Ui.ChoicePage, Task<IReadOnlyList<Ui.Choice<T>>>> find, IEnumerable<Ui.Choice<T>>? held = null)
    {
        Ui.Choice<T>[] shown = [.. held ?? []];
        Choose((before, leading) => (UiChoices.Paged(find, shown, UiChoices.Lead<T>(leading), mayBeEmpty: false, before, out var changed, pills: true), changed));
        return this;
    }

    /// <summary>
    ///     Pins <paramref name="rows" /> before the rows <c>.Choices(…)</c> offers, in the order given, never sorted among
    ///     them.
    /// </summary>
    /// <remarks>
    ///     As on <c>Ui.Select</c>: written after <c>.Choices(…)</c>, kept by every search — never hidden for what is
    ///     typed, put before every answer's first rows and never asked of the source — and given values no choice holds.
    ///     <code>
    ///     Ui.Pillbox.Bind(() =&gt; filter.CompanyIds).Choices(companies).Leading(new Ui.Choice&lt;int&gt;(0, "No company"))
    ///     </code>
    /// </remarks>
    /// <param name="rows">The rows, in the order they stand first.</param>
    /// <returns>This pillbox.</returns>
    public UiPillbox<T> Leading(params Ui.Choice<T>[] rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        (_choices ??= new UiChoosing()).Lead(this, rows.ToArray(), "Ui.Pillbox");
        return this;
    }

    private void Choose(Func<UiChosen?, object?, (UiChosen Chosen, bool Changed)> step) => (_choices ??= new UiChoosing()).Take(this, step);

    /// <inheritdoc />
    void ICarriesHandSteps.CarryTo(Component kept)
    {
        if (kept is UiPillbox<T> other && _choices is { } mine)
        {
            mine.CarryTo(other, other._choices ??= new UiChoosing());
        }
    }
}
