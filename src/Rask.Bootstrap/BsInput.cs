using Rask.Core.Forms;

namespace Rask.Bootstrap;

// A Bootstrap text input. Wraps the core Input and adds the .form-control class, an optional label,
// help text, and the .is-invalid + .invalid-feedback validation display. Bound:
// BsInput(() => model.Email, Label: "Email"); controlled: BsInput(Value: x, OnChange: …). The HTML
// input type is derived from T (or set explicitly via Type).
public sealed class BsInput<T> : BsFormControl<T>
{
    public InputType? Type { get; set; }
    public string? Placeholder { get; set; }
    public bool? ReadOnly { get; set; }
    public string? Autocomplete { get; set; }

    protected override Component? Render()
    {
        var b = Resolve();
        var controlId = ControlId(b);
        var cls = BsClass.Join("form-control", SizeClass("form-control"), b.Invalid ? "is-invalid" : null, Class);

        // Floating labels only animate when the control has a placeholder (the :placeholder-shown
        // selector drives the effect); fall back to the label text when none is given.
        var placeholder = Floating is true ? Placeholder ?? Label ?? " " : Placeholder;

        var control = Input<string>(
            Type: DeriveType(),
            Name: Name ?? b.Accessor?.PropertyName,
            Value: BindingHelpers.FormatValue(b.Current),
            Placeholder: placeholder, Disabled: Disabled, ReadOnly: ReadOnly, Required: Required,
            Autocomplete: Autocomplete, Class: cls, Id: controlId,
            OnInputAsync: Disabled == true ? null : StringChangeHandler(b));

        return Field(controlId, b, control);
    }

    private InputType? DeriveType()
    {
        if (Type is { } t)
        {
            return t;
        }

        var u = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        if (u == typeof(string))
        {
            return null;
        }

        if (u == typeof(DateOnly))
        {
            return InputType.Date;
        }

        if (u == typeof(TimeOnly) || u == typeof(TimeSpan))
        {
            return InputType.Time;
        }

        if (u == typeof(DateTime) || u == typeof(DateTimeOffset))
        {
            return InputType.DatetimeLocal;
        }

        return u.IsPrimitive || u == typeof(decimal) ? InputType.Number : InputType.Text;
    }
}
