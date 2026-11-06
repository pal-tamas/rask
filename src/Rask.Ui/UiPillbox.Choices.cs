namespace Rask;

// `.Choices(…)`: several rows of another table, picked out of a list the pillbox draws from rows rather than options.
public sealed partial class UiPillbox<T>
{
    // What the last `.Choices(…)` said; null for a pillbox of options written as children.
    private UiChosen? _choosing;

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
        _choosing = UiChoices.Held(choices, mayBeEmpty: false, _choosing, out var changed, pills: true);
        Changed(changed);
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
        _choosing = UiChoices.Asked(find, held ?? [], mayBeEmpty: false, _choosing, out var changed, pills: true);
        Changed(changed);
        return this;
    }

    private void Changed(bool changed)
    {
        if (changed)
        {
            BuilderRuntime.MarkChanged(this);
        }
    }
}
