namespace Rask;

/// <summary>
/// What a <see cref="UiSkeletonGroup" /> tells the skeletons inside it: how they animate.
/// </summary>
/// <remarks>
/// The skeletons are written by the caller, often several components down — inside a table's cells — so the
/// group has no call site at which to hand each one its animation. It provides this, and each reads it.
/// </remarks>
internal sealed record UiSkeletonScope(Ui.SkeletonAnimate Animate)
{
    /// <summary>The animation a skeleton draws: its own, or else its group's, or else none.</summary>
    internal static Ui.SkeletonAnimate Resolve(Ui.SkeletonAnimate? own) =>
        own ?? Context.Get<UiSkeletonScope>()?.Animate ?? Ui.SkeletonAnimate.None;

    /// <summary>The classes that animate a skeleton's root.</summary>
    /// <remarks>
    /// The shimmer is a <c>::before</c> as wide as the skeleton, parked one width to the left and carried two
    /// widths right by <c>ui-shimmer</c> (ui.css): a gradient that is <c>--ui-shimmer-color</c> at half
    /// strength in the middle and clear at both ends. The pulse is Tailwind's own.
    /// </remarks>
    internal static string Class(Ui.SkeletonAnimate animate) => animate switch
    {
        Ui.SkeletonAnimate.Shimmer =>
            "relative overflow-hidden [--ui-shimmer-color:white] dark:[--ui-shimmer-color:var(--color-zinc-900)] "
            + "before:absolute before:inset-0 before:z-10 before:-translate-x-full before:bg-linear-to-r "
            + "before:from-transparent before:via-(--ui-shimmer-color)/50 before:to-transparent "
            + "before:animate-[ui-shimmer_2s_infinite]",
        Ui.SkeletonAnimate.Pulse => "animate-pulse",
        _ => "",
    };
}
