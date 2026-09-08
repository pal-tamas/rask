namespace Rask.Ui;

/// <summary>
/// A checkbox and the label that says what ticking it means.
/// </summary>
/// <remarks>
/// The label wraps the box rather than sitting beside it, so the words are part of the hit target — on a
/// phone, a 16px box on its own is the difference between a control and a dare.
/// </remarks>
public sealed partial class UiCheckbox : Component
{
    /// <summary>
    ///     The words beside the control, daisyUI's <c>label-text</c>. Not <c>Label</c>, which MaryUI uses:
    ///     this renders a &lt;label&gt; element and a property of that name would shadow its chain entry.
    ///     <c>new</c> because the base type has a markup entry called <c>Text</c>, which this does not use.
    /// </summary>
    public new required string Text { get; set; }

    public bool? Checked { get; set; }

    public UiTone? Tone { get; set; }

    public UiSize? Size { get; set; }

    public bool? Disabled { get; set; }

    public Action<bool>? OnChange { get; set; }

    public Func<bool, Task>? OnChangeAsync { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        // Of<bool>() rather than a value: the type argument is what makes OnChange an
        // Action<bool>, and this control reports a bool.
        var box = Input.Of<bool>()
            .Checked(Checked == true)
            .Disabled(Disabled == true)
            .Class(UiClass.Compose(
                "checkbox",
                Tone is { } tone ? UiClassNames.CheckboxTone(tone) : "",
                Size is { } size ? UiClassNames.CheckboxSize(size) : ""));

        if (OnChangeAsync is { } async)
        {
            box = box.OnChangeAsync(async);
        }
        else if (OnChange is { } sync)
        {
            box = box.OnChange(sync);
        }

        return Label.Class(UiClass.Compose("label cursor-pointer gap-2", Class))[box, Span[Text]];
    }
}
