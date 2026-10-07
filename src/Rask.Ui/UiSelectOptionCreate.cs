namespace Rask;

/// <summary>
///     Flux's <c>flux:select.option.create</c>: the row of a <see cref="UiSelect{T}" /> that makes a new option
///     out of what was typed.
/// </summary>
/// <remarks>
///     <para>
///     <c>Ui.SelectOptionCreate.MinLength(2).OnClick(name =&gt; Create(name))["Create new"]</c>. The select shows it
///     once the search is <see cref="MinLength" /> long and names no option that is already there, and hides the
///     "no results" row while it does. Picking it — a click, or Enter while it is the active row — hands
///     <see cref="OnClick" /> the search as typed; the page adds the option and selects it.
///     </para>
///     <para>
///     In a listbox with no search it is always shown, and <see cref="OnClick" /> is handed an empty string:
///     that is Flux's <c>modal</c> case, where the page opens a form of its own.
///     </para>
/// </remarks>
public sealed partial class UiSelectOptionCreate : Component
{
    /// <summary>How long the search must be before the row is offered.</summary>
    public int? MinLength { get; set; }

    /// <summary>Called with the search as typed when the row is picked: Flux's <c>wire:click</c>.</summary>
    public Callback<string> OnClick { get; set; }

    /// <summary>Classes for the call site, added to the row's own.</summary>
    public string? Class { get; set; }

    // The select draws the row; on its own it is nothing.
    /// <inheritdoc />
    protected override Component? Render() => null;
}
