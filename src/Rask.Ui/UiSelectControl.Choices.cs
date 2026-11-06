namespace Rask;

// Rows of another table handed to the select by `.Choices(…)`: the select draws them as its options, or asks for them
// as it asks `Search`, and fills in what a picker of rows wants unless the page says otherwise — a listbox with a
// search field, the catalog's "Choose...", and a clear button where choosing nothing is an answer.
//
// Not Flux's: an addition the owner approved (2026-11-06) for the picker both apps wrote of their own. Flux's select
// is the same, its options written out by the page.
public abstract partial class UiSelectControl<T>
{
    // What the last `.Choices(…)` said; null for a select of options written as children.
    private UiChosen? _choosing;

    /// <summary>The options the select draws: the rows <c>.Choices(…)</c> handed it, then the children the page wrote.</summary>
    private IEnumerable<Component?>? Written => _choosing is { } choosing ? [.. choosing.Rows, .. Children ?? []] : Children;

    /// <summary>The search the page answers: its own <see cref="Search" />, or the one <c>.Choices(…)</c> asks.</summary>
    private Fn<string, Task<IEnumerable<Component?>>>? Asked => Search ?? _choosing?.Find;

    /// <summary>What the select shows while nothing is picked: the page's words, or the catalog's for a select of rows.</summary>
    private string? Placeholding => Placeholder ?? (_choosing is null ? null : UiChoices.Placeholder);

    /// <summary>Whether a clear button is asked for: by the page, or by rows a member that may hold nothing is bound to.</summary>
    private bool ClearsByItself => (Clearable ?? _choosing?.MayBeEmpty) == true;

    /// <summary>Whether a search field is asked for: by the page, or by rows, which a picker always searches.</summary>
    private bool SearchesByItself => Searchable ?? _choosing is not null;

    /// <summary>What the last render's <c>.Choices(…)</c> made, for this one to compare with.</summary>
    private protected UiChosen? Chosen => _choosing;

    /// <summary>Takes what <c>.Choices(…)</c> made, and tells the render cache when it is not what the last render had.</summary>
    private protected void Choose(UiChosen chosen, bool changed)
    {
        if (changed)
        {
            BuilderRuntime.MarkChanged(this);
        }

        _choosing = chosen;
    }
}
