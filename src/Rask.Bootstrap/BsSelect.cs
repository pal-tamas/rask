using Rask.Core.Forms;

namespace Rask.Bootstrap;

// A Bootstrap select. Wraps the core Select with the .form-select class, label, help text and
// validation display. Pass Option(...) children. Bound: BsSelect(() => model.Plan, Label: "Plan")[…].
public sealed class BsSelect<T> : BsFormControl<T>
{
    protected override Component? Render()
    {
        var b = Resolve();
        var controlId = ControlId(b);
        var cls = BsClass.Join("form-select", SizeClass("form-select"), b.Invalid ? "is-invalid" : null, Class);

        var control = Select<string>(
            Name: Name ?? b.Accessor?.PropertyName,
            Value: BindingHelpers.FormatValue(b.Current),
            Disabled: Disabled, Required: Required, Class: cls, Id: controlId, Aria: FieldAria(b, controlId),
            OnChangeAsync: Disabled == true ? null : StringChangeHandler(b))[Items];

        return Field(controlId, b, control);
    }
}
