namespace Rask.Ui;

/// <summary>
/// A switch. The same input as <see cref="UiCheckbox" />, drawn as a toggle.
/// </summary>
/// <remarks>
/// Drawn differently and meant differently: a checkbox states a fact that is submitted later, a toggle
/// reads as taking effect now. Use it where flipping it does something.
/// </remarks>
public sealed partial class UiToggle : Component
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
        var box = Input
            .Value(Checked == true)
            .Disabled(Disabled == true)
            .Class(UiClass.Compose(
                "toggle",
                Tone is { } tone ? UiClassNames.ToggleTone(tone) : "",
                Size is { } size ? UiClassNames.ToggleSize(size) : ""));

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
