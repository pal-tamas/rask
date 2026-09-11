using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Ui;

/// <summary>
/// A multi-line text field.
/// </summary>
/// <remarks>
/// A form control, like every input in the kit: <c>.Bind(() =&gt; model.Notes)</c> two-way binds and
/// drives the surrounding <c>Form</c>'s validation, or <c>Value</c> with <c>OnChange</c>
/// leaves the value with the parent. The opening step fixes both the type argument and the mode.
/// </remarks>
public sealed partial class UiTextarea<T> : UiFormField<T>
{
    /// <summary>
    ///     Shown in the empty field. Defaults to the label when the label floats — see <see cref="Floating" />.
    /// </summary>
    public string? Placeholder { get; set; }

    /// <inheritdoc cref="UiInput{T}.Floating" />
    public bool? Floating { get; set; }

    /// <inheritdoc />
    private protected override bool FloatsLabel => Floating != false;

    private string PlaceholderText => Placeholder ?? (Label is not null && FloatsLabel ? Label : string.Empty);

    public int? Rows { get; set; }

    /// <inheritdoc cref="UiInput{T}.OnInput" />
    public Callback<string>? OnInput { get; set; }

    /// <inheritdoc cref="UiInput{T}.Name" />
    public string? Name { get; set; }

    /// <summary>
    ///     daisyUI defines only <see cref="UiVariant.Ghost" /> for a text control — the borderless form
    ///     that shows its edges on focus. The rest draw the default rather than a class that does nothing.
    /// </summary>

    /// <inheritdoc />
    /// <inheritdoc />
    protected override Component Control()
    {
        if (Bind is { } bind)
        {
            return Textarea
                .Bind(bind)
                .Id(FieldId)
                .Name(Name)
                .OnInput(OnInput)
                .Validate(Validate)
                .AfterBind(AfterBind)
                .Placeholder(PlaceholderText)
                .Rows(Rows ?? 3)
                .Aria(ControlAria())
                .Disabled(Disabled == true)
                .Class(BoxClass());
        }

        return Textarea
            .Value(Value)
            .Id(FieldId)
            .Name(Name)
            .OnInput(OnInput)
            .OnChange(OnChange)
            .Placeholder(PlaceholderText)
            .Rows(Rows ?? 3)
            .Aria(ControlAria())
            .Disabled(Disabled == true)
            .Class(BoxClass());
    }

    private string BoxClass() =>
        UiClass.Compose(
            "textarea validator",
            Tone is { } tone ? UiClassNames.TextareaTone(tone) : "",
            Variant is { } variant ? UiClassNames.TextareaVariant(variant) : "",
            Size is { } size ? UiClassNames.TextareaSize(size) : "",
            Class);
}
