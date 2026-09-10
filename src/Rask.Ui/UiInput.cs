using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Ui;

/// <summary>
/// A text field.
/// </summary>
/// <remarks>
/// <para>
/// daisyUI's <c>input</c>, with the label, the hint and the validation message coming from
/// <see cref="UiFormField{T}" /> — so a field is one component rather than three siblings a call site has
/// to keep associated. <c>UiFormField&lt;T&gt;.Tone</c> colours the border, which is how a field says it
/// is in error without a second element, and the message under it says why.
/// </para>
/// <para>
/// A form control: <c>.Bind(() =&gt; model.Email)</c> two-way binds and drives the surrounding
/// <c>Form</c>'s validation, or <c>Value</c> with <c>OnChange</c> lets the parent own it. The opening step
/// fixes both the type argument and the mode, so a call site reads <c>UiInput.Bind(…).Label(…)</c> or
/// <c>UiInput.Value(…).Label(…)</c>.
/// </para>
/// <para>
/// <typeparamref name="T" /> is whatever the field holds — a <c>string</c>, an <c>int</c>, a
/// <c>DateOnly</c>. The parse in both directions is the framework's, not this component's: it forwards
/// to <c>Rask.Core</c>'s <c>Input&lt;T&gt;</c>, which is itself an <c>IFormControl&lt;T&gt;</c>.
/// </para>
/// </remarks>
public sealed partial class UiInput<T> : UiFormField<T>
{
    public string? Placeholder { get; set; }

    public InputType? Type { get; set; }

    /// <inheritdoc />
    /// <inheritdoc />
    protected override Component Control()
    {
        // Bound and controlled are different chain TYPES, not two settings on one — Bind and Value are
        // mutually exclusive openings — so each is built as its own complete expression.
        if (Bind is { } bind)
        {
            return Input
                .Bind(bind)
                .Validate(Validate)
                .AfterBind(AfterBind)
                .Id(Id)
            .Type(Type)
                .Placeholder(Placeholder ?? string.Empty)
                .Aria(ControlAria())
                .Disabled(Disabled == true)
                .Class(BoxClass());
        }

        return Input
            .Value(Value)
            .OnChange(OnChange)
            .Id(Id)
            .Type(Type)
            .Placeholder(Placeholder ?? string.Empty)
            .Aria(ControlAria())
            .Disabled(Disabled == true)
            .Class(BoxClass());
    }

    private string BoxClass() =>
        UiClass.Compose(
            "input validator",
            Tone is { } tone ? UiClassNames.InputTone(tone) : "",
            Size is { } size ? UiClassNames.InputSize(size) : "",
            Variant is { } variant ? UiClassNames.InputVariant(variant) : "",
            Class);
}
