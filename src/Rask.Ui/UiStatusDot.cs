namespace Rask.Ui;

/// <summary>
/// A filled dot and what it means — the quietest way to say a state.
/// </summary>
/// <remarks>
/// The label is required rather than optional. Colour alone is not a status: someone who cannot
/// distinguish the amber from the teal would otherwise be reading an unlabelled dot.
/// </remarks>
public sealed partial class UiStatusDot : Component
{
    public new required string Label { get; set; }

    /// <summary>The dot's colour. Omitted, it reads as idle.</summary>
    public UiTone? Tone { get; set; }

    public UiSize? Size { get; set; }

    /// <summary>Pulses, for a state that is still moving.</summary>
    public bool? Animated { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Span.Class("inline-flex items-center gap-1.5 whitespace-nowrap text-xs opacity-70")[
            Span
                .Class(UiClass.Compose(
                    "status",
                    Tone is { } tone ? UiClassNames.StatusTone(tone) : "",
                    Size is { } size ? UiClassNames.StatusSize(size) : "",
                    Animated == true ? "animate-pulse" : ""))
                .Attributes(("aria-hidden", "true")),
            Span[Label]
        ];
}
