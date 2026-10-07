using System.Linq.Expressions;

namespace Rask;

/// <summary>What a <see cref="UiField" /> tells the parts inside it: which control they belong to.</summary>
/// <param name="ControlId">The control's id, when the field found one among its children.</param>
/// <param name="Bound">The model member that control is bound to.</param>
/// <param name="HasDescription">Whether the field holds a <see cref="UiDescription" />.</param>
/// <param name="HasError">Whether the field holds a <see cref="UiError" />.</param>
internal sealed record UiFieldScope(string? ControlId, LambdaExpression? Bound, bool HasDescription, bool HasError)
{
    /// <summary>Inside a fieldset and outside any field: nothing to wire.</summary>
    internal static UiFieldScope None { get; } = new(null, null, false, false);

    internal string? LabelId => ControlId is null ? null : UiFieldId.Label(ControlId);

    internal string? DescriptionId => ControlId is null ? null : UiFieldId.Description(ControlId);

    internal string? ErrorId => ControlId is null ? null : UiFieldId.Error(ControlId);

    /// <summary>
    ///     Reads a field's direct children: a kit control says who it is, a plain element is known by its id.
    /// </summary>
    internal static UiFieldScope Of(IEnumerable<Component?>? children)
    {
        string? controlId = null;
        LambdaExpression? bound = null;
        var description = false;
        var error = false;

        foreach (var child in children ?? [])
        {
            switch (child)
            {
                case UiDescription:
                    description = true;
                    break;
                case UiError:
                    error = true;
                    break;
                case IUiFieldControl control when controlId is null:
                    (controlId, bound) = (control.ControlId, control.Bound);
                    break;
                case Element { Id: { } id } when controlId is null:
                    controlId = id;
                    break;
            }
        }

        return new UiFieldScope(controlId, bound, description, error);
    }
}
