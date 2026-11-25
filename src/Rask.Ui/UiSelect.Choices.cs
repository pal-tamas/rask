using Rask.Core.Forms;

namespace Rask;

// `.Choices(…)`: one row of another table, picked out of a list the select draws from rows rather than options.
public sealed partial class UiSelect<T>
{
    /// <summary>
    ///     Offers <paramref name="choices" />: a listbox with a search field over them, in the reader's alphabet, the
    ///     catalog's "Choose..." while nothing is picked, and a clear button where choosing nothing is an answer.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Not Flux's: an addition the owner approved (2026-11-06) for a picker of rows. What it fills in is only
    ///         a default — <c>.Placeholder(…)</c>, <c>.Clearable(…)</c>, <c>.Searchable(false)</c> or <c>.Combobox</c>
    ///         say otherwise, and options written as children come after the rows.
    ///     </para>
    ///     <para>
    ///         A member that may hold nothing (<c>int?</c>, <c>string?</c>) is cleared with the X, unless the model
    ///         requires it — the question <c>aria-required</c> asks — and a member that cannot (<c>int</c>) never
    ///         is. A required member's message is the model's own rule's.
    ///     </para>
    ///     <code>
    ///     Ui.Select.Bind(() =&gt; model.CompanyId).Choices(companies)
    ///     </code>
    /// </remarks>
    /// <param name="choices">The rows, in any order: <c>new Ui.Choice&lt;int&gt;(c.Id, c.Name)</c>.</param>
    /// <typeparam name="TValue">The type of a row's value: the select's own, or what a nullable select holds.</typeparam>
    /// <returns>This select.</returns>
    public UiSelect<T> Choices<TValue>(IEnumerable<Ui.Choice<TValue>> choices)
        where TValue : T => Offer(choices);

    /// <summary>
    ///     Offers the rows <paramref name="find" /> answers for what is typed — the best few from the database, for a
    ///     list too long to send whole — as <see cref="UiSelectControl{T}.Search" /> does, in the reader's alphabet.
    /// </summary>
    /// <remarks>
    ///     As <see cref="Choices{TValue}(IEnumerable{Ui.Choice{TValue}})" />, and as <c>Search</c>: asked for <c>""</c>
    ///     when the select mounts and once typing pauses, and the picked row stays first in the list while a search does
    ///     not bring it.
    ///     <code>
    ///     Ui.Select.Bind(() =&gt; model.DriverId).Choices(Driver.Named, held: driver)
    ///     </code>
    /// </remarks>
    /// <param name="find">The rows for what was typed, trimmed: the first few for nothing typed.</param>
    /// <param name="held">The row the value holds, so the select names it before anything is found.</param>
    /// <typeparam name="TValue">The type of a row's value: the select's own, or what a nullable select holds.</typeparam>
    /// <returns>This select.</returns>
    public UiSelect<T> Choices<TValue>(Func<string, Task<IReadOnlyList<Ui.Choice<TValue>>>> find, Ui.Choice<TValue>? held = null)
        where TValue : T => Offer(find, held);

    /// <summary>
    ///     Offers the rows <paramref name="find" /> answers for what is typed a page at a time: the first page as the list
    ///     opens and once typing pauses, and the next one as the reader scrolls near the end of the rows already here — for
    ///     a list too long to send whole that is still scrolled through to its last row.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         As <see cref="Choices{TValue}(Func{string, Task{IReadOnlyList{Ui.Choice{TValue}}}}, Ui.Choice{TValue}?)" />,
    ///         but asked for <see cref="Ui.ChoicePage" />s: an answer of fewer rows than <see cref="Ui.ChoicePage.Take" />
    ///         is the end of the list. A page answered as <see cref="Ui.ChoiceRows{T}" /> says how many rows there are in all: the
    ///         list is that long from its first page, its scrollbar the whole list's, and the rows a scroll reaches are asked for
    ///         at once. The rows are drawn in the order <paramref name="find" /> gives them, page after
    ///         page — order the query (<c>OrderBy</c>) so the pages follow on. Typing starts again from the first page, and
    ///         a page asked for older words is dropped.
    ///     </para>
    ///     <code>
    ///     Ui.Select.Bind(() =&gt; model.DriverId).Choices((typed, page) =&gt; Driver.Named(typed, page), held: driver)
    ///     </code>
    /// </remarks>
    /// <param name="find">The page of rows for what was typed, trimmed: <c>query.Skip(page.Skip).Take(page.Take)</c>.</param>
    /// <param name="held">The row the value holds, so the select names it before anything is found.</param>
    /// <typeparam name="TValue">The type of a row's value: the select's own, or what a nullable select holds.</typeparam>
    /// <returns>This select.</returns>
    public UiSelect<T> Choices<TValue>(Func<string, Ui.ChoicePage, Task<IReadOnlyList<Ui.Choice<TValue>>>> find, Ui.Choice<TValue>? held = null)
        where TValue : T => Offer(find, held);

    /// <summary>
    ///     Offers the rows of a read face — <c>.Choices(Company.Choices(c =&gt; c.Name))</c> — searched and paged in the
    ///     database as the reader types and scrolls, the scrollbar the whole list's, and the picked row named even when it
    ///     is on no page the reader has reached.
    /// </summary>
    /// <remarks>
    ///     As the paging <see cref="Choices{TValue}(Func{string, Ui.ChoicePage, Task{IReadOnlyList{Ui.Choice{TValue}}}}, Ui.Choice{TValue}?)" />,
    ///     with nothing to write around it: the source (<see cref="IChoiceSource{TKey}" />) searches case and accents aside,
    ///     in the app's text order, through the tenant in flight, and names the picked row on the first page of every
    ///     answer, so no <c>held:</c> is read by the page. A row of another tenant's, or of none, is never named.
    ///     <code>
    ///     Ui.Select.Bind(() =&gt; model.CompanyId).Choices(Company.Choices(c =&gt; c.Name))
    ///     Ui.Select.Bind(() =&gt; model.DriverId).Choices(Driver.Where(d =&gt; d.Active).Choices(d =&gt; d.Id, d =&gt; d.Name, d =&gt; d.Licence))
    ///     </code>
    /// </remarks>
    /// <param name="source">Where the rows come from: a read face's <c>X.Choices(…)</c>, or a source of the app's own.</param>
    /// <typeparam name="TValue">The type of a row's id: the select's own, or what a nullable select holds.</typeparam>
    /// <returns>This select.</returns>
    public UiSelect<T> Choices<TValue>(IChoiceSource<TValue> source)
        where TValue : T => Offer(source);

    /// <summary>
    ///     Pins <paramref name="rows" /> before the rows <c>.Choices(…)</c> offers — "No company" first — in the order given,
    ///     never sorted among them.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Written after <c>.Choices(…)</c>, over any of its sources. Every search keeps them: a list held whole never
    ///         hides them for what is typed, and a searched or paged one puts them before every answer's first rows,
    ///         whatever was typed — they are never asked of the source, and a paged source's first page is asked for that
    ///         many fewer rows, so the list's first screen holds as many. A picked row a search left out still comes first.
    ///         Give them values no choice holds. Not Flux's: the owner's (2026-11-18).
    ///     </para>
    ///     <code>
    ///     Ui.Select.Bind(() =&gt; filter.CompanyId).Choices(companies).Leading(new Ui.Choice&lt;int&gt;(0, "No company"))
    ///     </code>
    /// </remarks>
    /// <param name="rows">The rows, in the order they stand first.</param>
    /// <typeparam name="TValue">The type of a row's value: the choices' own.</typeparam>
    /// <returns>This select.</returns>
    public UiSelect<T> Leading<TValue>(params Ui.Choice<TValue>[] rows)
        where TValue : T => Lead(rows);

    // Unconstrained, for UiSelectChoices too: a select of `int?` takes rows of `int`, which C# cannot say as a constraint.
    // The rows are read once, here: a Key written after this step replays it onto the instance the key keeps.
    internal UiSelect<T> Offer<TValue>(IEnumerable<Ui.Choice<TValue>> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        Ui.Choice<TValue>[] rows = [.. choices];
        var mayBeEmpty = MayBeEmpty;
        Choose((before, leading) => (UiChoices.Held(rows, UiChoices.Lead<TValue>(leading), mayBeEmpty, before, out var changed), changed));
        return this;
    }

    internal UiSelect<T> Offer<TValue>(Func<string, Task<IReadOnlyList<Ui.Choice<TValue>>>> find, Ui.Choice<TValue>? held)
    {
        Ui.Choice<TValue>[] shown = held is { } row ? [row] : [];
        var mayBeEmpty = MayBeEmpty;
        Choose((before, leading) => (UiChoices.Asked(find, shown, UiChoices.Lead<TValue>(leading), mayBeEmpty, before, out var changed), changed));
        return this;
    }

    internal UiSelect<T> Offer<TValue>(Func<string, Ui.ChoicePage, Task<IReadOnlyList<Ui.Choice<TValue>>>> find, Ui.Choice<TValue>? held)
    {
        Ui.Choice<TValue>[] shown = held is { } row ? [row] : [];
        var mayBeEmpty = MayBeEmpty;
        Choose((before, leading) => (UiChoices.Paged(find, shown, UiChoices.Lead<TValue>(leading), mayBeEmpty, before, out var changed), changed));
        return this;
    }

    internal UiSelect<T> Offer<TValue>(IChoiceSource<TValue> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var mayBeEmpty = MayBeEmpty;
        Choose((before, leading) =>
        {
            var rows = UiSourceChoices<TValue>.Of(before, source, Picked<TValue>);
            return (UiChoices.Paged(rows.Find, rows.Held(), UiChoices.Lead<TValue>(leading), mayBeEmpty, before, out var changed), changed);
        });
        return this;
    }

    // The id the select holds as it asks: its bound member's, or the value the page hands it; none while it holds none.
    private TValue[] Picked<TValue>()
    {
        var value = Bind is { } bind ? ExpressionAccessor.Parse(bind).Getter() : Value;
        return value is TValue held ? [held] : [];
    }

    // Unconstrained, as Offer is. Copied, so a page that fills the array it handed over later changes nothing here.
    internal UiSelect<T> Lead<TValue>(Ui.Choice<TValue>[] rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        Pin(rows.ToArray(), "Ui.Select");
        return this;
    }

    private bool MayBeEmpty => UiChoices.MayBeEmpty(Bind, typeof(T));
}
