using System.Linq.Expressions;

namespace Rask;

/// <summary>
/// Flux UI's input group: an input fused to what sits beside it —
/// <c>Ui.InputGroup[Ui.InputGroupPrefix["https://"], Ui.Input.Bind(() =&gt; m.Site)]</c>.
/// </summary>
/// <remarks>
/// <para>
/// Its children share one outline: only the two ends keep their corners, and a border is drawn once
/// between neighbours. An input takes the room left over. A neighbour of another kind — a button, a select —
/// joins by carrying <c>data-ui-group-target</c> on its bordered element.
/// </para>
/// <para>
/// For a label, put the GROUP in a <c>Ui.Field</c>, not a <c>Label</c> on the input: the group stands for its
/// input there, so the field's label, description and error reach it.
/// </para>
/// </remarks>
public sealed partial class UiInputGroup : Component, IUiFieldControl
{
    // A target is the bordered element: the <input> inside its wrapper, or a direct child that is one.
    private const string Look = "flex w-full " + Fuse;

    /// <summary>Joins the bordered elements of a row into one box: inner corners square, one line between two.</summary>
    internal const string Fuse =
        "[&>[data-ui-input]]:grow "
        + "[&>[data-ui-input]:not(:first-child)_[data-ui-group-target]]:rounded-s-none "
        + "[&>[data-ui-input]:not(:first-child)_[data-ui-group-target]]:border-s-0 "
        + "[&>[data-ui-input]:not(:last-child)_[data-ui-group-target]]:rounded-e-none "
        + "[&>[data-ui-input]:has(+[data-ui-input-group-suffix])_[data-ui-group-target]]:border-e-0 "
        + "[&>[data-ui-group-target]:not(:first-child)]:rounded-s-none "
        + "[&>[data-ui-group-target]:not(:first-child)]:border-s-0 "
        + "[&>[data-ui-group-target]:not(:last-child)]:rounded-e-none";

    /// <summary>Classes for the group: widths and margins.</summary>
    public string? Class { get; set; }

    string IUiFieldControl.ControlId => Inner?.ControlId ?? "f-field";

    LambdaExpression? IUiFieldControl.Bound => Inner?.Bound;

    // The control the group is for: the first one in it.
    private IUiFieldControl? Inner => Children?.OfType<IUiFieldControl>().FirstOrDefault();

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose(Look, Class)).Data("ui-input-group", "")[
            Context.Provide(Scope())[Children ?? []]
        ];

    // Tells the control inside that a field is composed around it — the one the group sits in, or none — so it
    // draws no field of its own between its neighbours.
    private UiFieldScope Scope()
    {
        var around = Context.Get<UiFieldScope>();
        var inner = Inner;
        var same = inner is not null && string.Equals(around?.ControlId, inner.ControlId, StringComparison.Ordinal);

        return new UiFieldScope(inner?.ControlId, inner?.Bound, same && around!.HasDescription, same && around!.HasError);
    }
}
