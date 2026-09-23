namespace Rask;

/// <summary>
/// Body copy, in the kit's type scale and inks.
/// </summary>
/// <remarks>
/// Flux UI's text. A paragraph by default; <see cref="Inline" /> makes it a <c>&lt;span&gt;</c> for a run inside another
/// line. <see cref="Strong" /> is the emphasised ink and <see cref="Subtle" /> the muted one; <see cref="Tone" /> colours
/// it outright, for a status line.
/// </remarks>
public sealed partial class UiText : Component
{
    /// <summary>How big it looks.</summary>
    public Ui.Size? Size { get; set; }

    /// <summary>Full ink and a heavier weight, for the part of a paragraph that matters.</summary>
    public bool? Strong { get; set; }

    /// <summary>Muted ink, for the part of a paragraph that can be skipped.</summary>
    public bool? Subtle { get; set; }

    /// <summary>Colours the text.</summary>
    public Ui.Tone? Tone { get; set; }

    /// <summary>A <c>&lt;span&gt;</c> rather than a paragraph.</summary>
    public bool? Inline { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var classes = UiClass.Compose(
            UiClassNames.TextSize(Size ?? Ui.Size.Default),
            Tone is { } tone
                ? UiClassNames.TextTone(tone)
                : Strong == true
                    ? "text-base-content font-medium"
                    : Subtle == true ? "text-base-content/60" : "text-base-content/80",
            Class);

        return Inline == true ? Span.Class(classes)[Children ?? []] : P.Class(classes)[Children ?? []];
    }
}
