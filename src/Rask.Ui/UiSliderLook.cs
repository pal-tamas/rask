using System.Text.RegularExpressions;

namespace Rask;

/// <summary>
///     How Flux's slider is drawn, and where each part sits for a value.
/// </summary>
/// <remarks>
///     <para>
///     The thumb's centre travels from half a thumb inside one end of the track to half a thumb inside the other,
///     so every position is <c>thumb / 2 + fraction × (track − thumb)</c>. The thumb's size is the one length the
///     rest has to know, and it is a custom property, <c>--ui-slider-thumb</c>.
///     </para>
///     <para>
///     Shared by every <see cref="UiSlider{T}" />: a static in a generic type is one copy per type argument.
///     </para>
/// </remarks>
internal static partial class UiSliderLook
{
    internal const string Root =
        "flex flex-col justify-center w-full min-h-4 [--ui-slider-thumb:1rem] has-[input:disabled]:opacity-50";

    internal const string Wrapper = "flex flex-col justify-center";

    internal const string Track =
        "@container relative shrink-0 [:where(&)]:h-1.5 rounded-full bg-zinc-200 dark:bg-white/10";

    internal const string Clip = "relative size-full rounded-full overflow-hidden";

    internal const string Indicator = "absolute inset-y-0 bg-zinc-800 dark:bg-white";

    internal const string Thumb =
        "absolute top-1/2 -translate-x-1/2 -translate-y-1/2 [:where(&)]:size-4 rounded-full bg-white "
        + "shadow-[0_1px_2px_0_rgba(0,0,0,0.05),0_2px_4px_0_rgba(0,0,0,0.1)] ring-1 ring-black/15 dark:ring-black/30 "
        + "focus-within:z-10 has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-[color:-webkit-focus-ring-color]";

    // The control itself: invisible, laid over the stretch of track its thumb can reach, with a thumb of no
    // width — so a press anywhere on it lands the value under the pointer, as a press on Flux's track does.
    internal const string Control =
        "absolute top-0 h-full m-0 px-[50%] box-border appearance-none bg-transparent opacity-0 cursor-default "
        + "[&::-webkit-slider-thumb]:appearance-none [&::-webkit-slider-thumb]:w-0 "
        + "[&::-moz-range-thumb]:w-0 [&::-moz-range-thumb]:border-0";

    // A row of labels sits further under the track than a row of lines does.
    internal const string TicksBelow = "relative grid cursor-default mt-2 has-[[data-ui-slider-tick-line]]:mt-1";

    internal const string TicksInside = "absolute inset-0 grid cursor-default";

    internal const string Tick =
        "relative col-start-1 row-start-1 flex flex-col items-center justify-center w-4 min-w-4 min-h-4 -translate-x-1/2 "
        + "text-xs font-medium whitespace-nowrap cursor-default";

    internal const string TickBelow = "text-zinc-400 data-active:text-zinc-500 dark:text-white/70 dark:data-active:text-white";

    // On the track: over the fill while the fill reaches it, over the bare track past the thumb.
    internal const string TickInside = "text-black/25 data-active:text-white/50 dark:text-white/25 dark:data-active:text-white/50";

    internal const string TickLine = "block w-px h-1 bg-black/25 dark:bg-white/25";

    internal const string TickDot = "block shrink-0 size-1 rounded-full bg-current";

    private const string Size = "var(--ui-slider-thumb)";

    /// <summary>Where a thumb's centre sits along the track, measured from its start.</summary>
    internal static string At(decimal fraction) =>
        $"calc({Size} / 2 + {UiSliderScale.Text(fraction)} * (100% - {Size}))";

    /// <summary>The filled stretch: from the start to the one thumb, or between the two.</summary>
    internal static string Filled(decimal[] fractions) => fractions.Length == 2
        ? $"inset-inline-start:{At(fractions[0])};width:calc({UiSliderScale.Text(fractions[1] - fractions[0])} * (100% - {Size}))"
        : $"inset-inline-start:0;width:{At(fractions[0])}";

    /// <summary>
    ///     The control's box inside its thumb: from the lowest point the thumb can reach to the highest, half a
    ///     thumb past each. Written in the track's width (<c>100cqw</c>), because the thumb is its containing block.
    /// </summary>
    internal static string Reach(decimal fraction, decimal low, decimal high) =>
        $"inset-inline-start:calc({UiSliderScale.Text(low - fraction)} * {Travel});"
        + $"width:calc({Size} + {UiSliderScale.Text(high - low)} * {Travel})";

    /// <summary>
    ///     Which half of the gap between two thumbs a control answers for: a press on the track then moves the
    ///     nearer thumb, each through its own control.
    /// </summary>
    internal static string Half(int thumb, decimal middle, decimal low, decimal high) => thumb == 0
        ? $";clip-path:inset(0 calc({Size} / 2 + {UiSliderScale.Text(high - middle)} * {Travel}) 0 0)"
        : $";clip-path:inset(0 0 0 calc({Size} / 2 + {UiSliderScale.Text(middle - low)} * {Travel}))";

    /// <summary>
    ///     <c>--ui-slider-thumb</c> for a thumb the call site resized with <c>size-6</c> or <c>size-[22px]</c>, so the
    ///     fill and the ticks follow it. Null for the 16px thumb.
    /// </summary>
    internal static string? ThumbSize(string? thumbClass)
    {
        if (thumbClass is null || SizeClass().Match(thumbClass) is not { Success: true } size)
        {
            return null;
        }

        return size.Groups["length"].Success
            ? $"--ui-slider-thumb:{size.Groups["length"].Value.Replace('_', ' ')}"
            : $"--ui-slider-thumb:calc(0.25rem * {size.Groups["units"].Value})";
    }

    private const string Travel = $"(100cqw - {Size})";

    [GeneratedRegex(@"(?:^|\s)size-(?:\[(?<length>[^\]]+)\]|(?<units>\d+(?:\.\d+)?))(?:\s|$)", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex SizeClass();
}
