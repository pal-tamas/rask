namespace Rask;

/// <summary>
///     Flux's <c>flux:select.group</c>: options of a <see cref="UiSelect{T}" /> under one heading.
/// </summary>
/// <remarks>
///     <c>Ui.SelectGroup.Label("Creative")[Ui.SelectOption.Value("design")["Design services"]]</c>. The select
///     draws it: an <c>&lt;optgroup&gt;</c> in the native variant, a headed run of rows otherwise.
/// </remarks>
public sealed partial class UiSelectGroup : Component
{
    /// <summary>The heading over the group's options.</summary>
    public string? Label { get; set; }

    // Outside a select there is no list to head: the browser's own group.
    /// <inheritdoc />
    protected override Component? Render() => Optgroup.Label(Label)[Children ?? []];
}
