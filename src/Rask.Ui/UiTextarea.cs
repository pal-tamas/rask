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

    public string? Placeholder { get; set; }

    public int? Rows { get; set; }

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
                .Validate(Validate)
                .AfterBind(AfterBind)
                .Placeholder(Placeholder ?? string.Empty)
                .Rows(Rows ?? 3)
                .Aria(ControlAria())
                .Disabled(Disabled == true)
                .Class(BoxClass());
        }

        return Textarea
            .Value(Value)
            .Id(FieldId)
            .OnChange(OnChange)
            .Placeholder(Placeholder ?? string.Empty)
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
