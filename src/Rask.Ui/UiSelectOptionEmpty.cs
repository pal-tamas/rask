namespace Rask;

/// <summary>
///     Flux's <c>flux:select.option.empty</c>: what a searchable <see cref="UiSelect{T}" /> says when the search
///     matches nothing.
/// </summary>
/// <remarks>
///     <c>Ui.SelectOptionEmpty.WhenLoading("Loading projects...")["No projects found."]</c>, as a child of the
///     select: Flux's <c>empty</c> slot. For the words alone, <c>Ui.Select.Empty("…")</c> is shorter.
/// </remarks>
public sealed partial class UiSelectOptionEmpty : Component
{
    /// <summary>What the row says while results are on their way. "Loading..." when unset.</summary>
    public string? WhenLoading { get; set; }

    /// <summary>Classes for the call site, added to the row's own.</summary>
    public string? Class { get; set; }

    // The select draws the row; on its own it is nothing.
    /// <inheritdoc />
    protected override Component? Render() => null;
}
