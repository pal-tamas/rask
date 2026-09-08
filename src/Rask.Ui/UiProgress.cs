namespace Rask.Ui;

/// <summary>
/// Work in progress, with a known amount left.
/// </summary>
/// <remarks>
/// A real <c>&lt;progress&gt;</c>: it reports its own value to assistive technology, which a styled div
/// has to be told to do and usually is not.
/// </remarks>
public sealed partial class UiProgress : Component
{
    /// <summary>The accessible name — what is progressing.</summary>
    public required string Label { get; set; }

    public required double Value { get; set; }

    public double? Max { get; set; }

    public UiTone? Tone { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Progress
            .Value(Value)
            .Max(Max ?? 100)
            .Aria(new Dictionary<string, string?> { ["label"] = Label })
            .Class(UiClass.Compose(
                "progress",
                Tone is { } tone ? UiClassNames.ProgressTone(tone) : "",
                Class));
}
