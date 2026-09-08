namespace Rask.Ui;

/// <summary>
/// A file picker.
/// </summary>
public sealed partial class UiFileInput : Component
{
    /// <summary>
    ///     daisyUI and MaryUI both call this <c>label</c>. Free to use here because this component renders no
    ///     &lt;label&gt; element of its own — where one does, the property is AccessibleLabel instead.
    /// </summary>
    public required string Label { get; set; }

    public UiTone? Tone { get; set; }

    /// <summary>
    ///     daisyUI defines only <see cref="UiVariant.Ghost" /> for a file input. The rest draw the
    ///     default rather than a class that does nothing.
    /// </summary>
    public UiVariant? Variant { get; set; }

    public UiSize? Size { get; set; }

    public bool? Disabled { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Input
            .Value(string.Empty)
            .Type(InputType.File)
            // aria-invalid is what makes daisyUI reveal a following UiValidator, and what a screen
            // reader needs: a field that is visibly red and says nothing is half a message. It is
            // OMITTED rather than nulled — a null renders the attribute valueless, and a valueless
            // aria-invalid reads as "true", which would mark every field in the kit invalid.
            .Aria(Tone == UiTone.Error
                ? new Dictionary<string, string?> { ["label"] = Label, ["invalid"] = "true" }
                : new Dictionary<string, string?> { ["label"] = Label })
            .Disabled(Disabled == true)
            .Class(UiClass.Compose(
                "file-input validator",
                Tone is { } tone ? UiClassNames.FileInputTone(tone) : "",
                Variant is { } variant ? UiClassNames.FileInputVariant(variant) : "",
                Size is { } size ? UiClassNames.FileInputSize(size) : "",
                Class));
}
