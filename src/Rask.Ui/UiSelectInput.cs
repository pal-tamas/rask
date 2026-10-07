namespace Rask;

/// <summary>
///     Flux's <c>flux:select.input</c>: the text input a combobox <see cref="UiSelect{T}" /> is typed into,
///     written out to hear what is typed.
/// </summary>
/// <remarks>
///     <para>
///     A child of the select — Flux's <c>input</c> slot. <see cref="OnInput" /> is where Flux writes
///     <c>wire:model.live="search"</c>: with <c>Filter(false)</c> on the select the page answers each
///     keystroke by rendering the options that match, from wherever it keeps them.
///     </para>
///     <code>
///     Ui.Select.Bind(() =&gt; m.UserId).Combobox.Filter(false)[
///         Ui.SelectInput.OnInput(text =&gt; _users = Users.Named(text)),
///         _users.Select(user =&gt; Ui.SelectOption.Key(user.Id).Value(user.Id)[user.Name])
///     ]
///     </code>
/// </remarks>
public sealed partial class UiSelectInput : Component
{
    /// <summary>Shown while the input is empty.</summary>
    public string? Placeholder { get; set; }

    /// <summary>True for error styling the form did not ask for.</summary>
    public bool? Invalid { get; set; }

    /// <summary>How tall the input is.</summary>
    public Ui.SelectSize? Size { get; set; }

    /// <summary>The search, when the page holds it: set it to empty to clear what was typed.</summary>
    public string? Value { get; set; }

    /// <summary>Called with the input's text on every keystroke.</summary>
    public Callback<string> OnInput { get; set; }

    /// <summary>Classes for the call site, added to the input's own.</summary>
    public string? Class { get; set; }

    // The select draws the input; on its own it is nothing.
    /// <inheritdoc />
    protected override Component? Render() => null;
}
