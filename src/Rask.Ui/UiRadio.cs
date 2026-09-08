namespace Rask.Ui;

/// <summary>
/// One option in a radio group.
/// </summary>
/// <remarks>
/// <see cref="Group" /> is required and is the browser's own grouping mechanism: radios with the same
/// <c>name</c> are mutually exclusive, and radios without one are not a group at all — they are several
/// independent controls that happen to look alike.
/// </remarks>
public sealed partial class UiRadio : Component
{
    /// <summary>
    ///     The words beside the control, daisyUI's <c>label-text</c>. Not <c>Label</c>, which MaryUI uses:
    ///     this renders a &lt;label&gt; element and a property of that name would shadow its chain entry.
    ///     <c>new</c> because the base type has a markup entry called <c>Text</c>, which this does not use.
    /// </summary>
    public new required string Text { get; set; }

    /// <summary>The <c>name</c> every option in the group shares. Without it there is no group.</summary>
    public required string Group { get; set; }

    public bool? Checked { get; set; }

    public UiTone? Tone { get; set; }

    public UiSize? Size { get; set; }

    public bool? Disabled { get; set; }

    public Action? OnSelected { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var box = Input.Of<bool>()
            .Checked(Checked == true)
            .Type(InputType.Radio)
            .Name(Group)
            .Disabled(Disabled == true)
            .Class(UiClass.Compose(
                "radio",
                Tone is { } tone ? UiClassNames.RadioTone(tone) : "",
                Size is { } size ? UiClassNames.RadioSize(size) : ""));

        if (OnSelected is { } selected)
        {
            // The bool is the input's own checked state, and a radio only ever reports true — selecting
            // one does not fire a change on the option it deselected. So the callback takes nothing:
            // "this option was chosen" is the whole event.
            box = box.OnChange(_ => selected());
        }

        return Label.Class(UiClass.Compose("label cursor-pointer gap-2", Class))[box, Span[Text]];
    }
}
