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

    // Unconstrained, for UiSelectChoices too: a select of `int?` takes rows of `int`, which C# cannot say as a constraint.
    internal UiSelect<T> Offer<TValue>(IEnumerable<Ui.Choice<TValue>> choices)
    {
        var chosen = UiChoices.Held(choices, MayBeEmpty, Chosen, out var changed);
        Choose(chosen, changed);
        return this;
    }

    internal UiSelect<T> Offer<TValue>(Func<string, Task<IReadOnlyList<Ui.Choice<TValue>>>> find, Ui.Choice<TValue>? held)
    {
        var chosen = UiChoices.Asked(find, held is { } row ? [row] : [], MayBeEmpty, Chosen, out var changed);
        Choose(chosen, changed);
        return this;
    }

    private bool MayBeEmpty => UiChoices.MayBeEmpty(Bind, typeof(T));
}
