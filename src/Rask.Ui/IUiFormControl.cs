namespace Rask;

/// <summary>
///     A Flux form control: what <see cref="UiWithField" /> reads to draw the field around it and to say what the
///     control tells assistive tech.
/// </summary>
/// <remarks>
///     Every member is the control's own public property of the same name, so implementing this is naming the
///     interface and declaring the properties Flux documents anyway.
/// </remarks>
internal interface IUiFormControl : IUiFieldControl
{
    /// <summary>Flux's <c>label</c>: wraps the control in a field with a label over it.</summary>
    string? Label { get; }

    /// <summary>Flux's <c>description</c>: help text between the label and the control.</summary>
    string? Description { get; }

    /// <summary>Flux's <c>description:trailing</c>: help text under the control.</summary>
    string? DescriptionTrailing { get; }

    /// <summary>Flux's <c>badge</c>: a word beside the label.</summary>
    string? Badge { get; }

    /// <summary>Flux's <c>invalid</c>: error styling the form did not ask for.</summary>
    bool? Invalid { get; }

    /// <summary>False to leave the bound member's message to a <c>Ui.Error</c> placed elsewhere.</summary>
    bool? ShowValidation { get; }
}
