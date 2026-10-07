namespace Rask;

/// <summary>
///     What <see cref="UiPillbox{T}" /> hands the list it is drawn over: the parts of a pillbox that a select
///     does not have.
/// </summary>
/// <remarks>
///     A pillbox is Flux's listbox with several answers under another trigger, so it is drawn by the control
///     that draws <c>Ui.Select</c>'s list; this says which trigger.
/// </remarks>
internal sealed class UiPillboxFace
{
    /// <summary>How long after the browser shut the list a click on the trigger is still the click that shut it.</summary>
    internal static readonly TimeSpan DismissedBy = TimeSpan.FromMilliseconds(250);

    /// <summary>True for Flux's <c>variant="combobox"</c>: an input among the pills, typed into to narrow the list.</summary>
    internal bool Combobox { get; init; }

    /// <summary>Flux's <c>trigger</c> slot, when it was written out.</summary>
    internal UiPillboxTrigger? Trigger { get; init; }
}
