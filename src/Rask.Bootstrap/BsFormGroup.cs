namespace Rask.Bootstrap;

// A spacing wrapper for one form field (Bootstrap's .mb-3 convention) — group a label, control and
// help text when you compose them by hand instead of using a BsInput/BsSelect's built-in layout.

/// <summary>
///     A labelled form field: the label, the control, its help text and its validation message as one unit,
///     spaced consistently.
/// </summary>
public sealed partial class BsFormGroup : BsBlock
{
    protected override Component? Render() => Div.Id(Id).Class(BsClass.Join("mb-3", Class))[Items];
}

// A Bootstrap form label: <label class="form-label">. Set For to tie it to a control id.

/// <summary>
///     A form label. Point <c>For</c> at the control's id — that association is what lets a click focus the
///     control and a screen reader name it.
/// </summary>
public sealed partial class BsFormLabel : BsBlock
{
    /// <summary>The id of the control this labels.</summary>
    public string? For { get; set; }

    protected override Component? Render() =>
        Label.For(For).Id(Id).Class(BsClass.Join("form-label", Class))[Items];
}
