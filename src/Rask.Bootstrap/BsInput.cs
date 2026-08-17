using Rask.Core.Forms;
using Rask.Html.Components;

namespace Rask.Bootstrap;

// A Bootstrap text input. Wraps the core Input and adds the .form-control class, an optional label,
// help text, and the .is-invalid + .invalid-feedback validation display. Bound:
// BsInput.Bind(() => model.Email).Label("Email"); controlled: BsInput<T>().Value(x).OnChange(…). The HTML
// input type is derived from T (or set explicitly via Type).

/// <summary>
///     A Bootstrap text input, bound to a model field. Its <c>Type</c> decides the on-screen keyboard and
///     the browser's own validation, so pick the specific one.
/// </summary>
public sealed partial class BsInput<T> : BsFormControl<T>
{
    /// <summary>Which control this is — text, email, number, date, and so on.</summary>
    public InputType? Type { get; set; }

    /// <summary>A hint shown while the field is empty. Never the only label.</summary>
    public string? Placeholder { get; set; }

    /// <summary>The value cannot be edited but is still focusable and still submitted.</summary>
    public bool? ReadOnly { get; set; }

    /// <summary>The kind of value expected, so the browser can fill it. Worth getting right.</summary>
    public string? Autocomplete { get; set; }

    // Constraint + input-affordance attributes forwarded verbatim to the core Input, so a Bootstrap
    // number/date/range input can set Min/Max/Step, a text field a Pattern/MaxLength/MinLength, a file
    // field Accept/Capture/Multiple, and any field the mobile-keyboard hints. (The HTML `size` attribute is
    // not exposed — the base `Size` is Bootstrap's control sizing, which is the far more common intent.)

    /// <summary>The smallest permitted value.</summary>
    public string? Min { get; set; }

    /// <summary>The largest permitted value.</summary>
    public string? Max { get; set; }

    /// <summary>The granularity the value must snap to.</summary>
    public string? Step { get; set; }

    /// <summary>A regular expression the value must match.</summary>
    public string? Pattern { get; set; }

    /// <summary>The most characters the user may enter.</summary>
    public int? MaxLength { get; set; }

    /// <summary>The fewest characters a valid value may have.</summary>
    public int? MinLength { get; set; }

    /// <summary>
    ///     Focuses this control on load. Disorienting for screen-reader users — reserve it for a page that
    ///     exists for this one field.
    /// </summary>
    public bool? Autofocus { get; set; }

    /// <summary>The id of a <c>datalist</c> supplying suggestions.</summary>
    public string? List { get; set; }

    /// <summary>
    ///     Which file types a file picker offers. A filter, not a guarantee — validate on the server.
    /// </summary>
    public string? Accept { get; set; }

    /// <summary>Asks a file input for the camera or microphone directly.</summary>
    public string? Capture { get; set; }

    /// <summary>Allows more than one value, for file and email inputs.</summary>
    public bool? Multiple { get; set; }

    /// <summary>Which virtual keyboard to show, without changing validation.</summary>
    public string? InputMode { get; set; }

    /// <summary>What the virtual keyboard's action key should say.</summary>
    public string? EnterKeyHint { get; set; }

    /// <summary>Whether the browser should spell-check the value.</summary>
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

        var control = Input
            .Value(BindingHelpers.FormatValue(b.Current))
            .Type(derivedType)
            .Name(Name ?? b.Accessor?.PropertyName)
            .Placeholder(placeholder)
            .Disabled(Disabled)
            .ReadOnly(ReadOnly)
            .Required(Required)
            .Min(Min)
            .Max(Max)
            .Pattern(Pattern)
            .MaxLength(MaxLength)
            .MinLength(MinLength)
            .Step(Step ?? (derivedType == InputType.Number ? BindingHelpers.DefaultStep(typeof(T)) : null))
            .Multiple(Multiple)
            .Accept(Accept)
            .Capture(Capture)
            .List(List)
            .Autofocus(Autofocus)
            .Autocomplete(Autocomplete)
            .InputMode(InputMode)
            .EnterKeyHint(EnterKeyHint)
            .Spellcheck(Spellcheck)
            .Class(cls)
            .Id(controlId)
            .Aria(FieldAria(b, controlId))
            .OnInputAsync(Disabled == true ? null : StringChangeHandler(b));

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
