using Rask.Core.Forms;

namespace Rask.Bootstrap;

// A Bootstrap textarea. Wraps the core Textarea with the .form-control class, label, help text and
// validation display. Bound: BsTextarea(() => model.Bio, Label: "Bio", Rows: 4).
public sealed class BsTextarea<T> : BsFormControl<T>
{
    public string? Placeholder { get; set; }
    public int? Rows { get; set; }
    public bool? ReadOnly { get; set; }

    protected override RenderResult Render()
    {
        var b = Resolve();
        var controlId = ControlId(b);
        var cls = BsClass.Join("form-control", SizeClass("form-control"), b.Invalid ? "is-invalid" : null, Class);

        var control = Textarea<string>(
            Name: Name ?? b.Accessor?.PropertyName,
            Value: BindingHelpers.FormatValue(b.Current),
            Placeholder: Placeholder, Rows: Rows, Disabled: Disabled, ReadOnly: ReadOnly, Required: Required,
            Class: cls, Id: controlId,
            OnInputAsync: Disabled == true ? null : StringChangeHandler(b));

        return Field(controlId, b, control);
    }
}
