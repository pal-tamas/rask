namespace Rask;

/// <summary>
///     Flux's <c>flux:pillbox.option.empty</c>: what a <see cref="UiPillbox{T}" /> says when what was typed
///     matches nothing.
/// </summary>
/// <remarks>
///     <c>Ui.PillboxOptionEmpty.WhenLoading("Loading tags...")["No tags found."]</c>, as a child of the pillbox:
///     Flux's <c>empty</c> slot.
/// </remarks>
public sealed partial class UiPillboxOptionEmpty : Component
{
    /// <summary>What the row says while results are on their way. "Loading..." when unset.</summary>
    public string? WhenLoading { get; set; }

    /// <summary>Classes for the call site, added to the row's own.</summary>
    public string? Class { get; set; }

    // The pillbox draws the row; on its own it is nothing.
    /// <inheritdoc />
    protected override Component? Render() => null;
}
