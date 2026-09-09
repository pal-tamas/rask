using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Ui;

/// <summary>
/// A switch. The same input as <see cref="UiCheckbox" />, drawn as a toggle.
/// </summary>
/// <remarks>
/// <para>
/// Drawn differently and meant differently: a checkbox states a fact that is submitted later, a toggle
/// turns something on now.
/// </para>
/// <para>
/// A form control over a <c>bool</c>, concretely rather than generically — a switch's value is a bool
/// and nothing else. <c>.Bind(() =&gt; model.Alerts)</c> two-way binds; <see cref="Value" /> with
/// <see cref="OnChange" /> leaves it with the parent.
/// </para>
/// </remarks>
public sealed partial class UiToggle : Component, IFormControl<bool>
{
    /// <summary>
    ///     The words beside the control, daisyUI's <c>label-text</c>. Not <c>Label</c>: this renders a
    ///     &lt;label&gt; element and a property of that name would shadow its chain entry.
    /// </summary>
    public new required string Text { get; set; }

    public UiTone? Tone { get; set; }

    public UiSize? Size { get; set; }

    public bool? Disabled { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    /// <remarks>
    ///     Not nullable, and that is the interface rather than a choice here — see
    ///     <see cref="UiCheckbox.Value" />.
    /// </remarks>
    public bool Value { get; set; }

    /// <inheritdoc />
    public Callback<bool>? OnChange { get; set; }


    /// <inheritdoc />
    public Expression<Func<bool>>? Bind { get; set; }

    /// <inheritdoc />
    public Validate<bool>? Validate { get; set; }

    /// <inheritdoc />
    public ValidateAsync<bool>? ValidateAsync { get; set; }

    /// <inheritdoc />
    public Callback<bool>? AfterBind { get; set; }


    /// <inheritdoc />
    protected override Component? Render() =>
        Label.Class(UiClass.Compose("label cursor-pointer gap-2", Class))[Box(), Span[Text]];

    private Component Box()
    {
        if (Bind is { } bind)
        {
            // Bound mode derives the checked state from the model and installs its own write-back.
            return Input
                .Bind(bind)
                .Validate(Validate)
                .ValidateAsync(ValidateAsync)
                .AfterBind(AfterBind)
                .Disabled(Disabled == true)
                .Class(BoxClass());
        }

        return Input
            .Of<bool>()
            .Checked(Value)
            .OnChange(OnChange)
            .Disabled(Disabled == true)
            .Class(BoxClass());
    }

    private string BoxClass() =>
        UiClass.Compose(
            "toggle",
            Tone is { } tone ? UiClassNames.ToggleTone(tone) : "",
            Size is { } size ? UiClassNames.ToggleSize(size) : "");
}
