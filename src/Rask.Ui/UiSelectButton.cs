namespace Rask;

/// <summary>
///     Flux's <c>flux:select.button</c>: the button a listbox <see cref="UiSelect{T}" /> opens from, written out
///     to set what the select's own props do not reach.
/// </summary>
/// <remarks>
///     A child of the select — Flux's <c>button</c> slot:
///     <c>Ui.Select.Bind(() =&gt; m.Plan).Listbox[Ui.SelectButton.Placeholder("Choose…").Class("rounded-full!"), …]</c>.
///     What it leaves unset is the select's.
/// </remarks>
public sealed partial class UiSelectButton : Component
{
    /// <summary>Shown while nothing is picked.</summary>
    public string? Placeholder { get; set; }

    /// <summary>True for error styling the form did not ask for.</summary>
    public bool? Invalid { get; set; }

    /// <summary>How tall the button is.</summary>
    public Ui.SelectSize? Size { get; set; }

    /// <summary>True for a button that cannot be opened.</summary>
    public bool? Disabled { get; set; }

    /// <summary>True for a button that clears the answer, shown while there is one.</summary>
    public bool? Clearable { get; set; }

    /// <summary>Classes for the call site, added to the button's own.</summary>
    public string? Class { get; set; }

    // The select draws the button; on its own it is nothing.
    /// <inheritdoc />
    protected override Component? Render() => null;
}
