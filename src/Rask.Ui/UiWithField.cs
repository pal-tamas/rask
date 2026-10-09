using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask;

/// <summary>
///     The field a control draws around itself when it is given a label or a description — Flux's shorthand —
///     and the <c>aria-*</c> that ties the control to it.
/// </summary>
/// <remarks>
///     <para>
///     A control calls this from its <c>Render</c> and nothing else about fields:
///     </para>
///     <code>
///     var field = UiWithField.For(this);            // this : IUiFormControl
///     var input = Input.Id(field.ControlId).Data("ui-control", "").Aria(field.Aria);
///     return field.Wrap(input);
///     </code>
///     <para>
///     With no label and no description <see cref="Wrap" /> hands the control back alone, and
///     <see cref="Aria" /> then names the parts of the <see cref="UiField" /> the call site composed around it.
///     A BOUND control with neither is still wrapped, in a field holding the control and its
///     <see cref="UiError" />: a rule that fails has to say so somewhere, and a control inside a
///     <c>Ui.Field</c> of the call site's own leaves that to the field.
///     </para>
/// </remarks>
internal sealed class UiWithField
{
    private readonly LambdaExpression? _bound;
    private readonly string? _label;
    private readonly string? _description;
    private readonly string? _descriptionTrailing;
    private readonly string? _badge;
    private readonly string? _error;
    private readonly bool _showValidation;

    private UiWithField(IUiFieldControl control, Shorthand props)
    {
        (ControlId, _bound) = (control.ControlId, control.Bound);
        (_label, _description, _descriptionTrailing, _badge) = (props.Label, props.Description, props.DescriptionTrailing, props.Badge);
        (_error, _showValidation) = (props.Error, props.ShowValidation);
        Invalid = props.Invalid || _error is not null || HasMessages(_bound);
        Aria = BuildAria();
    }

    /// <summary>The id the control writes on its element; the label points at it.</summary>
    internal string ControlId { get; }

    /// <summary>The label's id, for a control a <c>&lt;label for&gt;</c> cannot name: <c>aria-labelledby</c>.</summary>
    internal string LabelId => UiFieldId.Label(ControlId);

    /// <summary><see cref="LabelId" /> while there is a label to point at — the control's own, or its field's.</summary>
    internal string? LabelledBy => _label is not null || ComposedAround() is not null ? LabelId : null;

    /// <summary>Whether the control was called invalid, or its bound member holds a message.</summary>
    internal bool Invalid { get; }

    /// <summary><c>aria-describedby</c> and <c>aria-invalid</c> for the control. Empty when there is neither.</summary>
    internal IReadOnlyDictionary<string, string?> Aria { get; }

    private bool Wraps => _label is not null || _description is not null || _descriptionTrailing is not null || _error is not null;

    private string TrailingId => UiFieldId.Description(ControlId) + "-trailing";

    /// <summary>The field for a Flux form control, read from its own props.</summary>
    /// <param name="control">The control: Flux's shorthand props, its id and what it is bound to.</param>
    internal static UiWithField For(IUiFormControl control) =>
        new(control, new Shorthand(
            control.Label,
            control.Description,
            control.DescriptionTrailing,
            control.Badge,
            null,
            control.Invalid == true,
            control.ShowValidation != false));

    /// <summary>The field for one control, from the shorthand props it was given.</summary>
    /// <param name="control">The control: its id and what it is bound to.</param>
    /// <param name="label">Flux's <c>label</c>.</param>
    /// <param name="description">Flux's <c>description</c>, under the label.</param>
    /// <param name="descriptionTrailing">Flux's <c>description:trailing</c>, under the control.</param>
    /// <param name="badge">Flux's <c>badge</c>, beside the label.</param>
    /// <param name="invalid">Flux's <c>invalid</c>, for a control with no binding to ask.</param>
    /// <param name="error">Flux's <c>error</c>: a message the call site states, for a control bound to nothing.</param>
    internal static UiWithField For(
        IUiFieldControl control,
        string? label = null,
        string? description = null,
        string? descriptionTrailing = null,
        string? badge = null,
        bool invalid = false,
        string? error = null) =>
        new(control, new Shorthand(label, description, descriptionTrailing, badge, error, invalid, true));

    /// <summary>The control inside its field, or alone when it was given nothing to draw one with.</summary>
    /// <param name="control">The control's own markup, carrying <see cref="ControlId" />.</param>
    /// <param name="variant">Inline for a checkbox, a radio, a switch.</param>
    /// <param name="controlFirst">Inline only: the control before its label rather than after.</param>
    internal Component Wrap(Component control, Ui.FieldVariant variant = Ui.FieldVariant.Block, bool controlFirst = false)
    {
        if (!Wraps)
        {
            return control;
        }

        var label = _label is null ? null : Ui.Label.Id(LabelId).For(ControlId).Badge(_badge)[_label];
        var description = _description is null ? null : Ui.Description.Id(UiFieldId.Description(ControlId))[_description];
        var trailing = _descriptionTrailing is null ? null : Ui.Description.Id(TrailingId)[_descriptionTrailing];
        var error = _showValidation ? Ui.Error.Id(UiFieldId.Error(ControlId)).For(_bound).Message(_error) : null;
        var field = Ui.Field.Variant(variant);

        return controlFirst
            ? field[control, label, description, error, trailing]
            : field[label, description, control, error, trailing];
    }

    private Dictionary<string, string?> BuildAria()
    {
        var aria = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (Invalid)
        {
            aria["invalid"] = "true";
        }

        // Error first: it is the news. Only while there is one — a hidden message would still be read out.
        var scope = Wraps ? null : ComposedAround();
        var ownError = Wraps && _showValidation;
        string?[] describedBy =
        [
            Invalid && (ownError || scope?.HasError == true) ? UiFieldId.Error(ControlId) : null,
            _description is not null || scope?.HasDescription == true ? UiFieldId.Description(ControlId) : null,
            _descriptionTrailing is not null ? TrailingId : null,
        ];

        if (describedBy.Any(id => id is not null))
        {
            aria["describedby"] = string.Join(' ', describedBy.Where(id => id is not null));
        }

        return aria;
    }

    // The Ui.Field the call site wrote around this control — not one further out, around some other control.
    private UiFieldScope? ComposedAround() =>
        Context.Get<UiFieldScope>() is { } scope && string.Equals(scope.ControlId, ControlId, StringComparison.Ordinal)
            ? scope
            : null;

    private static bool HasMessages(LambdaExpression? bound) =>
        bound is not null
        && EditContextScope.Current is { } form
        && form.GetValidationMessages(ExpressionAccessor.Parse(bound).Field).Count > 0;

    private readonly record struct Shorthand(
        string? Label,
        string? Description,
        string? DescriptionTrailing,
        string? Badge,
        string? Error,
        bool Invalid,
        bool ShowValidation);
}
