namespace Rask.Ui;

/// <summary>
/// A multi-line text field.
/// </summary>
public sealed partial class UiTextarea : Component
{
    /// <summary>
    ///     daisyUI and MaryUI both call this <c>label</c>. Free to use here because this component renders no
    ///     &lt;label&gt; element of its own — where one does, the property is AccessibleLabel instead.
    /// </summary>
    public required string Label { get; set; }

    public string? Value { get; set; }

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

    public Action<string>? OnChange { get; set; }

    public Func<string, Task>? OnChangeAsync { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var area = Textarea
            .Value(Value ?? string.Empty)
            .Placeholder(Placeholder ?? string.Empty)
            .Rows(Rows ?? 3)
            .Aria(new Dictionary<string, string?> { ["label"] = Label })
            .Disabled(Disabled == true)
            .Class(UiClass.Compose(
                "textarea",
                Tone is { } tone ? UiClassNames.TextareaTone(tone) : "",
                Variant is { } variant ? UiClassNames.TextareaVariant(variant) : "",
                Size is { } size ? UiClassNames.TextareaSize(size) : "",
                Class));

        if (OnChangeAsync is { } async)
        {
            return area.OnChangeAsync(async);
        }

        return OnChange is { } sync ? area.OnChange(sync) : area;
    }
}
