namespace Rask;

/// <summary>
/// The shape of content that has not arrived: a block, sized and rounded by the call site.
/// </summary>
/// <remarks>
/// <para>
/// Flux UI's <c>flux:skeleton</c>. On its own it is a 16px bar the width of its container;
/// <c>.Class("size-10 rounded-full")</c> makes it an avatar, <c>.Class("aspect-[4/1] rounded-lg")</c> a chart.
/// Its height and radius are written at zero specificity, so the call site's always win.
/// </para>
/// <para>
/// <see cref="Animate" /> left unset follows the <see cref="UiSkeletonGroup" /> around it. The shimmer's
/// light is <c>--ui-shimmer-color</c>: white, and <c>zinc-900</c> in dark — set it to the colour of the
/// surface behind the skeleton when that is neither.
/// </para>
/// </remarks>
public sealed partial class UiSkeleton : Component
{
    /// <summary>How it animates. Unset, it follows its group, and is still without one.</summary>
    public Ui.SkeletonAnimate? Animate { get; set; }

    /// <summary>Classes for the block: its size, its radius.</summary>
    public string? Class { get; set; }

    /// <summary>Inline CSS for the block — a width that is only known at run time.</summary>
    public string? Style { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div
            .Data("ui-skeleton", "")
            .Class(UiClass.Compose(
                "[:where(&)]:h-4 [:where(&)]:rounded-md bg-zinc-400/20",
                UiSkeletonScope.Class(UiSkeletonScope.Resolve(Animate)),
                Class))
            .Style(Style)[
            Children ?? []
        ];
}
