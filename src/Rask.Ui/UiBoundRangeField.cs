using System.Linq.Expressions;

namespace Rask;

/// <summary>
///     A range's one field, as <see cref="UiWithField" /> draws it: its label names the start's control, its badge and
///     its words are the range member's, and each end says its own message under itself.
/// </summary>
/// <param name="ControlId">The start's control, which the label points at.</param>
/// <param name="Bound">The range member, whose name labels the field and whose requiredness badges it.</param>
/// <param name="Look">The field's words.</param>
internal sealed record UiBoundRangeField(string ControlId, LambdaExpression Bound, UiBoundLook Look) : IUiFormControl
{
    LambdaExpression? IUiFieldControl.Bound => Bound;

    string? IUiFormControl.Label => Look.Label;

    string? IUiFormControl.Description => Look.Description;

    string? IUiFormControl.DescriptionTrailing => null;

    string? IUiFormControl.Badge => Look.Badge;

    bool? IUiFormControl.Invalid => null;

    bool? IUiFormControl.ShowValidation => false;
}
