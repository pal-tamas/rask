namespace Rask;

/// <summary>
/// A line of text that has not arrived.
/// </summary>
/// <remarks>
/// Flux UI's <c>flux:skeleton.line</c>. It takes up the whole line height of the text it stands in for and
/// draws a bar the height of the letters inside it, so a page keeps its rhythm when the words land. The
/// width is the container's; <c>.Class("w-1/2")</c> shortens it.
/// </remarks>
public sealed partial class UiSkeletonLine : Component
{
    /// <summary>The size of text it stands in for.</summary>
    public Ui.SkeletonLineSize? Size { get; set; }

    /// <summary>How it animates. Unset, it follows its group, and is still without one.</summary>
    public Ui.SkeletonAnimate? Animate { get; set; }

    /// <summary>Classes for the line: its width, the space under it.</summary>
    public string? Class { get; set; }

    /// <summary>Inline CSS for the line — a width that is only known at run time.</summary>
    public string? Style { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var large = Size == Ui.SkeletonLineSize.Lg;

        return Div
            .Data("ui-skeleton-line", "")
            .Class(UiClass.Compose(
                large ? "py-1" : "py-[3px]",
                UiSkeletonScope.Class(UiSkeletonScope.Resolve(Animate)),
                Class))
            .Style(Style)[
            Div.Class(large ? "h-4 rounded-sm bg-zinc-400/20" : "h-3.5 rounded-sm bg-zinc-400/20")
        ];
    }
}
