using System.Linq.Expressions;
using Rask.Core.Forms;

namespace Rask.Ui;

/// <summary>
/// One option of a set where exactly one may be chosen.
/// </summary>
/// <remarks>
/// <para>
/// A form control over its OWN <c>bool</c> — whether this option is the chosen one — rather than over
/// the group's value. That is what a single radio is: the group's value belongs to the group, and the
/// kit's control for a whole group is <see cref="UiFilter{T}" />, which binds the chosen option
/// itself.
/// </para>
/// <para>
/// <see cref="Group" /> is what makes a set of them exclusive, and it is the browser doing that rather
/// than anything here.
/// </para>
/// </remarks>
public sealed partial class UiRadio : Component, IFormControl<bool>
{
    /// <summary>
    ///     The words beside the control, daisyUI's <c>label-text</c>. Not <c>Label</c>: this renders a
    ///     &lt;label&gt; element and a property of that name would shadow its chain entry.
    /// </summary>
    public new required string Text { get; set; }

    /// <summary>The name that makes a set of these mutually exclusive.</summary>
    public required string Group { get; set; }

    public UiTone? Tone { get; set; }

    public UiSize? Size { get; set; }

    public bool? Disabled { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    /// <remarks>
    ///     Whether THIS option is the chosen one. Not nullable — see <see cref="UiCheckbox.Value" />.
    /// </remarks>
    public bool Value { get; set; }

    /// <inheritdoc />
    /// <remarks>
    ///     A radio only ever reports <see langword="true" />: choosing one fires no change on the option
    ///     it deselected, so the browser never tells that one it was turned off. A handler that treats
    ///     <see langword="false" /> as meaningful will wait forever for it.
    /// </remarks>
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
            return Input
                .Bind(bind)
                .Validate(Validate)
                .AfterBind(AfterBind)
                .Type(InputType.Radio)
                .Name(Group)
                .Disabled(Disabled == true)
                .Class(BoxClass());
        }

        return Input
            .Of<bool>()
            .Checked(Value)
            .OnChange(OnChange)
            .Type(InputType.Radio)
            .Name(Group)
            .Disabled(Disabled == true)
            .Class(BoxClass());
    }

    private string BoxClass() =>
        UiClass.Compose(
            "radio",
            Tone is { } tone ? UiClassNames.RadioTone(tone) : "",
            Size is { } size ? UiClassNames.RadioSize(size) : "");
}
