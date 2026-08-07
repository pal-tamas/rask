using Rask.Core.Forms;

namespace Rask.Bootstrap;

// A Bootstrap textarea. Wraps the core Textarea with the .form-control class, label, help text and
// validation display. Bound: BsTextarea(() => model.Bio, Label: "Bio", Rows: 4).
public sealed partial class BsTextarea<T> : BsFormControl<T>
{
    public string? Placeholder { get; set; }
    public int? Rows { get; set; }
    public bool? ReadOnly { get; set; }

    // Sizing + constraint attributes forwarded verbatim to the core Textarea. (The core Textarea's `wrap`
    // attribute is not surfaced here — the name collides with BsBlock.Wrap; use the core Textarea for it.)
    public int? Cols { get; set; }
    public int? MaxLength { get; set; }
    public int? MinLength { get; set; }
    public string? Autocomplete { get; set; }
    public bool? Autofocus { get; set; }

    protected override Component? Render()
    {
        var b = Resolve();
        var controlId = ControlId(b);
        var cls = BsClass.Join("form-control", SizeClass("form-control"), b.Invalid ? "is-invalid" : null, Class);

        var control = Rask.Core.Components.Generated.Textarea<string>(
            Name: Name ?? b.Accessor?.PropertyName,
            Value: BindingHelpers.FormatValue(b.Current),
            Placeholder: Placeholder, Rows: Rows, Cols: Cols, Disabled: Disabled, ReadOnly: ReadOnly,
            Required: Required, MaxLength: MaxLength, MinLength: MinLength,
            Autocomplete: Autocomplete, Autofocus: Autofocus,
            Class: cls, Id: controlId, Aria: FieldAria(b, controlId),
            OnInputAsync: Disabled == true ? null : StringChangeHandler(b));

        return Field(controlId, b, control);
    }
}
