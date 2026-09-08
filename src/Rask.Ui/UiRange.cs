namespace Rask.Ui;

/// <summary>
/// A slider.
/// </summary>
public sealed partial class UiRange : Component
{
    /// <summary>
    ///     daisyUI and MaryUI both call this <c>label</c>. Free to use here because this component renders no
    ///     &lt;label&gt; element of its own — where one does, the property is AccessibleLabel instead.
    /// </summary>
    /// <remarks>A slider with no name announces only a number.</remarks>
    public required string Label { get; set; }

    public double? Value { get; set; }

    public double? Min { get; set; }

    public double? Max { get; set; }

    public double? Step { get; set; }

    public UiTone? Tone { get; set; }

    public UiSize? Size { get; set; }

    /// <summary>
    ///     Stands the track on end. daisyUI rotates the control, so the low value is at the BOTTOM —
    ///     which is what a volume or a level wants and what a rank does not.
    /// </summary>
    public bool? Vertical { get; set; }

    /// <summary>
    ///     Runs as the handle moves, with the value the reader has landed on. Without it the control
    ///     draws a value and reports nothing, which is a slider you can push and cannot read.
    /// </summary>
    public Action<double>? OnChange { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var range = Input
            .Value((Value ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Type(InputType.Range)
            .Min((Min ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Max((Max ?? 100).ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Step((Step ?? 1).ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Aria(new Dictionary<string, string?> { ["label"] = Label })
            .Class(UiClass.Compose(
                "range",
                Tone is { } tone ? UiClassNames.RangeTone(tone) : "",
                Size is { } size ? UiClassNames.RangeSize(size) : "",
                Vertical == true ? "range-vertical" : "",
                Class));

        return OnChange is { } change
            // The browser hands back a string; a range whose value did not parse is a browser bug, and
            // falling back to the current value is quieter than throwing at a reader mid-drag.
            ? range.OnChange(text => change(
                double.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : Value ?? 0))
            : range;
    }
}
