namespace Rask;

/// <summary>
///     A pillbox's parts as the parts of the list that draws it: Flux's <c>flux:pillbox.*</c> are its
///     <c>flux:select.*</c> under the pillbox's names, with the props the pillbox documents.
/// </summary>
internal static class UiPillboxParts
{
    /// <summary>The select's part for <paramref name="child" />, or the child itself when it is no part of a pillbox.</summary>
    /// <param name="child">One child of a <see cref="UiPillbox{T}" />.</param>
    internal static Component? Translate(Component? child) => child switch
    {
        UiPillboxOption option => Ui.SelectOption
            .Key(option.Key)
            .Value(option.Value)
            .Label(option.Label)
            .SelectedLabel(option.SelectedLabel)
            .Disabled(option.Disabled)
            .Class(option.Class)
            .FilteredWhen(option.Filterable)[option.Children ?? []],
        UiPillboxOptionCreate create => Ui.SelectOptionCreate
            .MinLength(create.MinLength)
            .OnClick(create.OnClick)
            .Class(create.Class)[create.Children ?? []],
        UiPillboxOptionEmpty empty => Ui.SelectOptionEmpty
            .WhenLoading(empty.WhenLoading)
            .Class(empty.Class)[empty.Children ?? []],
        UiPillboxInput input => Ui.SelectInput
            .Placeholder(input.Placeholder)
            .Invalid(input.Invalid)
            .Value(input.Value)
            .OnInput(input.OnInput)
            .Class(input.Class),
        _ => child,
    };

    /// <summary>The search field: Flux's <c>search</c> slot, or the pillbox's own <c>search:placeholder</c>.</summary>
    /// <param name="search">The slot, when it was written out.</param>
    /// <param name="placeholder">The pillbox's <c>search:placeholder</c>, which the slot's own wins over.</param>
    internal static Component Search(UiPillboxSearch? search, string? placeholder) =>
        Ui.SelectSearch
            .Placeholder(search?.Placeholder ?? placeholder)
            .Icon(search?.Icon)
            .Clearable(search?.Clearable)
            .Value(search?.Value)
            .OnInput(search?.OnInput ?? default)
            .Class(search?.Class);
}
