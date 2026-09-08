namespace Rask.Ui;

/// <summary>
/// One key and its value, joined by a dotted leader.
/// </summary>
/// <remarks>
/// The leader is the reference's most recognisable detail, and it is also the thing that cannot survive a
/// narrow screen: a dashed rule between "Queued time" and a timestamp has nowhere to go at 360px. So below
/// <c>sm</c> the pair stacks and the leader is hidden outright, which is what it would have degenerated
/// into anyway.
/// </remarks>
public sealed partial class UiDetailRow : Component
{
    public required string Label { get; set; }

    public required string Value { get; set; }

    /// <summary>Monospace, for ids, sizes and durations — anything a machine produced.</summary>
    public bool? Mono { get; set; }

    /// <summary>
    /// <see cref="UiTone.Error" /> or <see cref="UiTone.Warning" /> to colour the value. Anything else
    /// reads as neutral.
    /// </summary>
    public UiTone? Tone { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var tone = Tone switch
        {
            UiTone.Error => "text-error",
            UiTone.Warning => "text-warning",
            _ => "text-base-content",
        };

        return Div.Class("flex flex-col gap-0.5 py-2.5 sm:flex-row sm:items-baseline sm:gap-3")[
            Span.Class("shrink-0 text-sm opacity-60")[Label],
            Span.Class("hidden min-w-4 grow translate-y-[-0.2rem] border-b border-dashed border-base-300 sm:block")
                .Attributes(("aria-hidden", "true")),
            Span.Class((Mono == true
                ? "break-all font-mono text-xs sm:shrink-0 sm:text-right "
                : "break-words text-sm sm:shrink-0 sm:text-right ") + tone)[Value]
        ];
    }
}
