using Rask.Core.Forms;

namespace Rask.Bootstrap;

// A Bootstrap text input. Wraps the core Input and adds the .form-control class, an optional label,
// help text, and the .is-invalid + .invalid-feedback validation display. Bound:
// BsInput(() => model.Email, Label: "Email"); controlled: BsInput(Value: x, OnChange: …). The HTML
// input type is derived from T (or set explicitly via Type).
public sealed partial class BsInput<T> : BsFormControl<T>
{
    public InputType? Type { get; set; }
    public string? Placeholder { get; set; }
    public bool? ReadOnly { get; set; }
    public string? Autocomplete { get; set; }

    // Constraint + input-affordance attributes forwarded verbatim to the core Input, so a Bootstrap
    // number/date/range input can set Min/Max/Step, a text field a Pattern/MaxLength/MinLength, a file
    // field Accept/Capture/Multiple, and any field the mobile-keyboard hints. (The HTML `size` attribute is
    // not exposed — the base `Size` is Bootstrap's control sizing, which is the far more common intent.)
    public string? Min { get; set; }
    public string? Max { get; set; }
    public string? Step { get; set; }
    public new string? Pattern { get; set; }
    public int? MaxLength { get; set; }
    public int? MinLength { get; set; }
    public bool? Autofocus { get; set; }
    public string? List { get; set; }
    public string? Accept { get; set; }
    public string? Capture { get; set; }
    public bool? Multiple { get; set; }
    public string? InputMode { get; set; }
    public string? EnterKeyHint { get; set; }
    public bool? Spellcheck { get; set; }

    protected override Component? Render()
    {
        var b = Resolve();
        var controlId = ControlId(b);
        var cls = BsClass.Join("form-control", SizeClass("form-control"), b.Invalid ? "is-invalid" : null, Class);

        // Floating labels only animate when the control has a placeholder (the :placeholder-shown
        // selector drives the effect); fall back to the label text when none is given.
        var placeholder = Floating is true ? Placeholder ?? Label ?? " " : Placeholder;

        // Derived here rather than inside Input<T>: this renders through Input<string> with a pre-formatted
        // value, so `typeof(T)` in there is `string` and its own default step would never see the decimal.
        var derivedType = DeriveType();

        var control = Rask.Core.Components.Generated.Input<string>(
            Type: derivedType,
            Name: Name ?? b.Accessor?.PropertyName,
            Value: BindingHelpers.FormatValue(b.Current),
            Placeholder: placeholder, Disabled: Disabled, ReadOnly: ReadOnly, Required: Required,
            Min: Min, Max: Max, Pattern: Pattern, MaxLength: MaxLength, MinLength: MinLength,
            Step: Step ?? (derivedType == InputType.Number ? BindingHelpers.DefaultStep(typeof(T)) : null),
            Multiple: Multiple, Accept: Accept, Capture: Capture, List: List, Autofocus: Autofocus,
            Autocomplete: Autocomplete, InputMode: InputMode, EnterKeyHint: EnterKeyHint, Spellcheck: Spellcheck,
            Class: cls, Id: controlId, Aria: FieldAria(b, controlId),
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
