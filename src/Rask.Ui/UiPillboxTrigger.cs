namespace Rask;

/// <summary>
///     Flux's <c>flux:pillbox.trigger</c>: the box a <see cref="UiPillbox{T}" /> shows its pills in and opens
///     from, written out to set what the pillbox's own props do not reach.
/// </summary>
/// <remarks>
///     A child of the pillbox — Flux's <c>trigger</c> slot:
///     <c>Ui.Pillbox.Bind(() =&gt; m.Tags)[Ui.PillboxTrigger.Placeholder("Choose…").Clearable(), …]</c>. What it
///     leaves unset is the pillbox's.
/// </remarks>
public sealed partial class UiPillboxTrigger : Component
{
    /// <summary>Shown while nothing is picked.</summary>
    public string? Placeholder { get; set; }

    /// <summary>True for error styling the form did not ask for.</summary>
    public bool? Invalid { get; set; }

    /// <summary>How tall the trigger is while it holds one line of pills.</summary>
    public Ui.PillboxSize? Size { get; set; }

    /// <summary>True for a button that takes every pill off, shown while there is one.</summary>
    public bool? Clearable { get; set; }

    /// <summary>Classes for the call site, added to the trigger's own.</summary>
    public string? Class { get; set; }

    // The pillbox draws the trigger; on its own it is nothing.
    /// <inheritdoc />
    protected override Component? Render() => null;
}
