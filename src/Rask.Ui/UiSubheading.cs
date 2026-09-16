namespace Rask.Ui;

/// <summary>
/// The quieter line under a <see cref="UiHeading" /> — what the section is for, in a sentence.
/// </summary>
/// <remarks>
/// Flux UI's subheading: muted, and never a heading itself, so it stays out of the document outline.
/// </remarks>
public sealed partial class UiSubheading : Component
{
    /// <summary>How big it looks.</summary>
    public UiSize? Size { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose(
            "text-base-content/60",
            UiClassNames.SubheadingSize(Size ?? UiSize.Default),
            Class))[Children ?? []];
}
