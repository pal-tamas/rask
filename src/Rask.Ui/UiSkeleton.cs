namespace Rask;

/// <summary>
/// The shape of content that has not arrived.
/// </summary>
/// <remarks>
/// <para>
/// <c>aria-hidden</c>, and deliberately: a placeholder has nothing to announce, and a screen reader
/// reading out a row of empty boxes is worse than silence. Pair it with a <see cref="UiLoading" /> where
/// the wait itself needs announcing.
/// </para>
/// <para>
/// <see cref="Lines" /> and <see cref="Circle" /> are Flux UI's shapes, and they exist because the alternative
/// is a class string at the call site — which is a class the kit's own stylesheet never compiled, so the
/// placeholder would render as an invisible zero-height box while the build stayed green.
/// </para>
/// </remarks>
public sealed partial class UiSkeleton : Component
{
    /// <summary>
    ///     Draws that many lines of text rather than one block, the last one short as a paragraph's is.
    /// </summary>
    public int? Lines { get; set; }

    /// <summary>Draws a circle — the placeholder for an avatar.</summary>
    public bool? Circle { get; set; }

    /// <summary>Holds its place without the shimmer, for a reader who asked for less motion.</summary>
    /// <remarks>
    ///     The kit's stylesheet already stops the animation under <c>prefers-reduced-motion</c>. This is for the
    ///     other case: a placeholder that will be on screen long enough for the shimmer to become a distraction
    ///     rather than a signal.
    /// </remarks>
    public bool? Animate { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        if (Lines is { } lines && lines > 0)
        {
            return Div.Class(UiClass.Compose("flex flex-col gap-2", Class)).Attributes(("aria-hidden", "true"))[
                Enumerable.Range(0, lines).Select(i =>
                    // The last line short, which is what a paragraph of text actually looks like — a stack of
                    // equal bars reads as a table, and the eye notices the difference before the content lands.
                    Div.Key(i).Class(UiClass.Compose(
                        Box(),
                        "h-4",
                        i == lines - 1 && lines > 1 ? "w-3/5" : "w-full")))
            ];
        }

        return Div
            .Class(UiClass.Compose(Box(), Circle == true ? "rounded-full" : "", Class))
            .Attributes(("aria-hidden", "true"))[
            Children ?? []
        ];
    }

    private string Box() => Animate == false ? "ui-skeleton-still skeleton" : "skeleton";
}
