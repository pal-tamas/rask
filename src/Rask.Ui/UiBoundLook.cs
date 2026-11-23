namespace Rask;

/// <summary>What a bound field hands every control it draws: the field's words and its state.</summary>
/// <param name="Label">The label the call site gave, or null for the member's own.</param>
/// <param name="Description">The help text: the call site's, else the member's <c>[Display(Description)]</c>.</param>
/// <param name="Placeholder">The placeholder: the call site's, else the member's <c>[Display(Prompt)]</c>.</param>
/// <param name="Badge">The badge the call site gave, or null for the automatic one.</param>
/// <param name="ShowValidation">False: the message is left to a <c>Ui.Error</c> of the page's own.</param>
/// <param name="Disabled">Whether the call site disabled the field.</param>
/// <param name="ReadOnly">Whether the member is <c>[Editable(false)]</c>.</param>
/// <param name="Multiline">Whether text is drawn as a textarea.</param>
/// <param name="Timing">When the control writes the model.</param>
internal sealed record UiBoundLook(
    string? Label,
    string? Description,
    string? Placeholder,
    string? Badge,
    bool? ShowValidation,
    bool Disabled,
    bool ReadOnly,
    bool Multiline,
    UiBoundTiming Timing)
{
    /// <summary>Disabled, for a control that cannot be read-only: what <c>[Editable(false)]</c> means for it.</summary>
    internal bool Locked => Disabled || ReadOnly;
}
