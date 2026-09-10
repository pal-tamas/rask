using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Ui;

/// <summary>
/// A checkbox and the label that says what ticking it means.
/// </summary>
/// <remarks>
/// <para>
/// The label wraps the box rather than sitting beside it, so the words are part of the hit target — on a
/// phone, a 16px box on its own is the difference between a control and a dare.
/// </para>
/// <para>
/// A form control over a <c>bool</c>, and concretely so rather than generic: a checkbox's value is a
/// bool and nothing else, so a type parameter here would have exactly one legal argument.
/// <c>.Bind(() =&gt; model.Agreed)</c> two-way binds; <see cref="Value" /> with <see cref="OnChange" />
/// leaves it with the parent.
/// </para>
/// </remarks>
public sealed partial class UiCheckbox : Component, IFormControl<bool>
{
    /// <summary>
    ///     The words beside the control, daisyUI's <c>label-text</c>. Not <c>Label</c>, which MaryUI uses:
    ///     this renders a &lt;label&gt; element and a property of that name would shadow its chain entry.
    ///     <c>new</c> because the base type has a markup entry called <c>Text</c>, which this does not use.
    /// </summary>
    public new required string Text { get; set; }

    public UiTone? Tone { get; set; }

    public UiSize? Size { get; set; }

    public bool? Disabled { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    /// <remarks>
    ///     <para>
    ///         Whether the box is ticked. This is the interface's <c>Value</c> rather than a
    ///         <c>Checked</c> of its own: a control with both would have two names for one state, and
    ///         only one of them could be what <c>Bind</c> writes to.
    ///     </para>
    ///     <para>
    ///         Not nullable, and that is the interface rather than a choice here: <c>IFormControl&lt;T&gt;</c>
    ///         declares <c>T? Value</c> over an unconstrained <c>T</c>, where <c>?</c> is a nullability
    ///         ANNOTATION — so closing it to a value type gives a plain <c>bool</c>. It is therefore a
    ///         required step of the controlled chain, which is no loss: a controlled control with no
    ///         value is a control whose state nobody owns.
    ///     </para>
    /// </remarks>
    public bool Value { get; set; }

    /// <inheritdoc />
    public Callback<bool>? OnChange { get; set; }


    /// <inheritdoc />
    public Expression<Func<bool>>? Bind { get; set; }

    /// <inheritdoc />
    public Validator<bool>? Validate { get; set; }


    /// <inheritdoc />
    public Callback<bool>? AfterBind { get; set; }


    /// <inheritdoc />
    protected override Component? Render() =>
        Label.Class(UiClass.Compose("label cursor-pointer gap-2", Class))[Box(), Span[Text]];

    private Component Box()
    {
        if (Bind is { } bind)
        {
            // Bound mode derives the checked state from the model and installs its own write-back, so
            // nothing here sets Checked — doing so would be a second source of truth for one field.
            return Input
                .Bind(bind)
                .Validate(Validate)
                .AfterBind(AfterBind)
                .Disabled(Disabled == true)
                .Class(BoxClass());
        }

        // Of<bool>() rather than a value: the type argument is what makes OnChange an Action<bool>, and
        // this control reports a bool. Checked is the state; Value on an <input> is the value attribute.
        return Input
            .Of<bool>()
            .Checked(Value)
            .OnChange(OnChange)
            .Disabled(Disabled == true)
            .Class(BoxClass());
    }

    private string BoxClass() =>
        UiClass.Compose(
            "checkbox",
            Tone is { } tone ? UiClassNames.CheckboxTone(tone) : "",
            Size is { } size ? UiClassNames.CheckboxSize(size) : "");
}
