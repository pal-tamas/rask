namespace Rask.Ui;

/// <summary>
/// A text field.
/// </summary>
/// <remarks>
/// daisyUI's <c>input</c>. <see cref="Tone" /> colours the border, which is how a field says it is in
/// error without a second element; <see cref="UiValidator" /> is the version that says why.
/// </remarks>
public sealed partial class UiInput : Component
{
    /// <summary>
    ///     daisyUI and MaryUI both call this <c>label</c>. Free to use here because this component renders no
    ///     &lt;label&gt; element of its own — where one does, the property is AccessibleLabel instead.
    /// </summary>
    /// <remarks>Rendered as <c>aria-label</c>: a placeholder is not a name, it vanishes when typing starts.</remarks>
    public required string Label { get; set; }

    public string? Value { get; set; }

    public string? Placeholder { get; set; }

    public InputType? Type { get; set; }

    public UiTone? Tone { get; set; }

    public UiSize? Size { get; set; }

    public UiVariant? Variant { get; set; }

    public bool? Disabled { get; set; }

    public Action<string>? OnChange { get; set; }

    public Func<string, Task>? OnChangeAsync { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var input = Input
            .Value(Value ?? string.Empty)
            .Type(Type ?? InputType.Text)
            .Placeholder(Placeholder ?? string.Empty)
            .Aria(new Dictionary<string, string?> { ["label"] = Label })
            .Disabled(Disabled == true)
            .Class(UiClass.Compose(
                "input",
                Tone is { } tone ? UiClassNames.InputTone(tone) : "",
                Size is { } size ? UiClassNames.InputSize(size) : "",
                Variant is { } variant ? UiClassNames.InputVariant(variant) : "",
                Class));

        if (OnChangeAsync is { } async)
        {
            return input.OnChangeAsync(async);
        }

        return OnChange is { } sync ? input.OnChange(sync) : input;
    }
}
