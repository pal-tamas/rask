using Rask.Core.Forms;

namespace Rask.Bootstrap;

// A Bootstrap textarea. Wraps the core Textarea with the .form-control class, label, help text and
// validation display. Bound: BsTextarea.Bind(() => model.Bio).Label("Bio").Rows(4).

/// <summary>
///     A Bootstrap multi-line text field, bound to a model field.
/// </summary>
public sealed partial class BsTextarea<T> : BsFormControl<T>
{
    /// <summary>A hint shown while the field is empty. Never the only label.</summary>
    public string? Placeholder { get; set; }

    /// <summary>The visible number of text lines.</summary>
    public int? Rows { get; set; }

    /// <summary>The value cannot be edited but is still submitted.</summary>
    public bool? ReadOnly { get; set; }

    // Sizing + constraint attributes forwarded verbatim to the core Textarea. (The core Textarea's `wrap`
    // attribute is not surfaced here — the name collides with BsBlock.Wrap; use the core Textarea for it.)

    /// <summary>The visible width in average character widths.</summary>
    public int? Cols { get; set; }

    /// <summary>The most characters the user may enter.</summary>
    public int? MaxLength { get; set; }

    /// <summary>The fewest characters a valid value may have.</summary>
    public int? MinLength { get; set; }

    /// <summary>The kind of value expected, so the browser can fill it.</summary>
    public string? Autocomplete { get; set; }

    /// <summary>Focuses this control on load.</summary>
    public bool? Autofocus { get; set; }

    protected override Component? Render()
    {
        var b = Resolve();
        var controlId = ControlId(b);
        var cls = BsClass.Join("form-control", SizeClass("form-control"), b.Invalid ? "is-invalid" : null, Class);

        var control = Textarea
            .Value(BindingHelpers.FormatValue(b.Current))
            .Name(Name ?? b.Accessor?.PropertyName)
            .Placeholder(Placeholder)
            .Rows(Rows)
            .Cols(Cols)
            .Disabled(Disabled)
            .ReadOnly(ReadOnly)
            .Required(Required)
            .MaxLength(MaxLength)
            .MinLength(MinLength)
            .Autocomplete(Autocomplete)
            .Autofocus(Autofocus)
            .Class(cls)
            .Id(controlId)
            .Aria(FieldAria(b, controlId))
            .OnInputAsync(Disabled == true ? null : StringChangeHandler(b));

        return Field(controlId, b, control);
    }
}
