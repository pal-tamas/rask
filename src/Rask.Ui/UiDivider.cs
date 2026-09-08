namespace Rask.Ui;

/// <summary>
/// A line between two parts of a page, optionally with a word on it.
/// </summary>
public sealed partial class UiDivider : Component
{
    /// <summary>The word on the line — "or", "then". Omitted, it is a plain rule.</summary>
    public new string? Text { get; set; }

    public UiTone? Tone { get; set; }

    /// <summary>Runs down instead of across. daisyUI's <c>divider-horizontal</c>.</summary>
    public bool? Vertical { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose(
            "divider",
            Vertical == true ? "divider-horizontal" : "",
            Tone is { } tone ? UiClassNames.DividerTone(tone) : "",
            Class))[Text];
}
