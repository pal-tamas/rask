using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Ui;

/// <summary>
/// A multi-line text field.
/// </summary>
/// <remarks>
/// A form control, like every input in the kit: <c>.Bind(() =&gt; model.Notes)</c> two-way binds and
/// drives the surrounding <c>Form</c>'s validation, or <see cref="Value" /> with <see cref="OnChange" />
/// leaves the value with the parent. The opening step fixes both the type argument and the mode.
/// </remarks>
public sealed partial class UiTextarea<T> : Component, IFormControl<T>
{
    /// <summary>
    ///     daisyUI and MaryUI both call this <c>label</c>. Free to use here because this component renders no
    ///     &lt;label&gt; element of its own — where one does, the property is AccessibleLabel instead.
    /// </summary>
    public required string Label { get; set; }

    public string? Placeholder { get; set; }

    public int? Rows { get; set; }

    public UiTone? Tone { get; set; }

    /// <summary>
    ///     daisyUI defines only <see cref="UiVariant.Ghost" /> for a text control — the borderless form
    ///     that shows its edges on focus. The rest draw the default rather than a class that does nothing.
    /// </summary>
    public UiVariant? Variant { get; set; }

    public UiSize? Size { get; set; }

    public bool? Disabled { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    public T? Value { get; set; }

    /// <inheritdoc />
    public Callback<T>? OnChange { get; set; }


    /// <inheritdoc />
    public Expression<Func<T>>? Bind { get; set; }

    /// <inheritdoc />
    public Validator<T>? Validate { get; set; }


    /// <inheritdoc />
    public Callback<T>? AfterBind { get; set; }


    /// <inheritdoc />
    protected override Component? Render()
    {
        if (Bind is { } bind)
        {
            return Textarea
                .Bind(bind)
                .Validate(Validate)
                .AfterBind(AfterBind)
                .Placeholder(Placeholder ?? string.Empty)
                .Rows(Rows ?? 3)
                .Aria(Aria())
                .Disabled(Disabled == true)
                .Class(BoxClass());
        }

        return Textarea
            .Value(Value)
            .OnChange(OnChange)
            .Placeholder(Placeholder ?? string.Empty)
            .Rows(Rows ?? 3)
            .Aria(Aria())
            .Disabled(Disabled == true)
            .Class(BoxClass());
    }

    // aria-invalid is OMITTED rather than nulled — a null renders the attribute valueless, and a
    // valueless aria-invalid reads as "true", which would mark every field in the kit invalid.
    private Dictionary<string, string?> Aria() =>
        Tone == UiTone.Error
            ? new Dictionary<string, string?> { ["label"] = Label, ["invalid"] = "true" }
            : new Dictionary<string, string?> { ["label"] = Label };

    private string BoxClass() =>
        UiClass.Compose(
            "textarea validator",
            Tone is { } tone ? UiClassNames.TextareaTone(tone) : "",
            Variant is { } variant ? UiClassNames.TextareaVariant(variant) : "",
            Size is { } size ? UiClassNames.TextareaSize(size) : "",
            Class);
}
