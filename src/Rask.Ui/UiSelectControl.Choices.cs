namespace Rask;

// Rows of another table handed to the select by `.Choices(…)`: the select draws them as its options, or asks for them
// as it asks `Search`, and fills in what a picker of rows wants unless the page says otherwise — a listbox with a
// search field, the catalog's "Choose...", and a clear button where choosing nothing is an answer.
//
// Not Flux's: an addition the owner approved (2026-11-06) for the picker both apps wrote of their own. Flux's select
// is the same, its options written out by the page.
public abstract partial class UiSelectControl<T>
{
    // What the last `.Choices(…)` said, and the step that said it; null for a select of options written as children.
    private UiChoosing? _choices;

    private UiChosen? Choosing => _choices?.Chosen;

    /// <summary>The options the select draws: the rows <c>.Choices(…)</c> handed it, then the children the page wrote.</summary>
    private IEnumerable<Component?>? Written => Choosing is { } choosing ? [.. choosing.Rows, .. Children ?? []] : Children;

    /// <summary>The search the page answers: its own <see cref="Search" />, or the one <c>.Choices(…)</c> asks.</summary>
    private Fn<string, Task<IEnumerable<Component?>>>? Asked => Search ?? Choosing?.Find;

    /// <summary>What the select shows while nothing is picked: the page's words, or the catalog's for a select of rows.</summary>
    private string? Placeholding => Placeholder ?? (Choosing is null ? null : UiChoices.Placeholder);

    /// <summary>Whether a clear button is asked for: by the page, or by rows a member that may hold nothing is bound to.</summary>
    private bool ClearsByItself => (Clearable ?? Choosing?.MayBeEmpty) == true;

    /// <summary>Whether a search field is asked for: by the page, or by rows, which a picker always searches.</summary>
    private bool SearchesByItself => Searchable ?? Choosing is not null;

    /// <summary>
    ///     Takes what <c>.Choices(…)</c> makes of the rows over what the last render made, and tells the render cache when
    ///     it is not that.
    /// </summary>
    private protected void Choose(Func<UiChosen?, (UiChosen Chosen, bool Changed)> step) => (_choices ??= new UiChoosing()).Take(this, step);

    /// <inheritdoc />
    void ICarriesHandSteps.CarryTo(Component kept)
    {
        if (kept is UiSelectControl<T> other && _choices is { } mine)
        {
            mine.CarryTo(other, other._choices ??= new UiChoosing());
        }
    }
}
