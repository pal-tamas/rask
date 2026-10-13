namespace Rask;

/// <summary>
///     The classes Flux's color picker is drawn with, each written from fluxui.dev's measurements.
/// </summary>
internal static class UiColorPickerLook
{
    /// <summary>The root: never narrower than 240px, unless the call site says so.</summary>
    internal const string Root = "block [:where(&)]:min-w-[240px]";

    /// <summary>The trigger's box, an input's: the row holding the swatch button and the text.</summary>
    internal const string Trigger =
        "flex items-center w-full overflow-hidden cursor-text " + UiInputLook.Outline + " " + UiInputLook.InputShadow
        + " has-disabled:shadow-none has-disabled:border-b-zinc-200 dark:has-disabled:border-white/5 dark:has-disabled:bg-white/[7%]"
        + " has-disabled:text-zinc-500 dark:has-disabled:text-zinc-400 has-disabled:cursor-default";

    /// <summary>The button trigger: the same box, pressed as a whole.</summary>
    internal const string ButtonTrigger =
        "flex items-center w-full overflow-hidden cursor-default border bg-white dark:bg-white/10 shadow-xs "
        + "border-zinc-200 border-b-zinc-300/80 dark:border-white/10 dark:border-b-white/10 "
        + "data-invalid:border-red-500 dark:data-invalid:border-red-500";

    /// <summary>The swatch button inside the input trigger.</summary>
    internal const string SwatchButton = "shrink-0 me-2 cursor-pointer disabled:opacity-50";

    /// <summary>The swatch of the button trigger, which is not a button of its own.</summary>
    internal const string SwatchBox = "shrink-0 me-2";

    /// <summary>The square showing the colour.</summary>
    internal const string Preview = "size-full rounded border border-black/10 dark:border-white/10 in-disabled:opacity-65";

    /// <summary>The trigger's text input: the rest of the row, with no box of its own.</summary>
    internal const string TriggerInput =
        "flex-1 min-w-0 appearance-none bg-transparent outline-none "
        + "placeholder:text-zinc-400 disabled:placeholder:text-zinc-400/70 dark:disabled:placeholder:text-zinc-500";

    /// <summary>The clear button at the end of the input trigger: Flux's small subtle button, as a <c>div</c>.</summary>
    internal const string Clear =
        "relative flex shrink-0 items-center justify-center gap-2 size-8 ms-1 rounded-md text-sm font-medium whitespace-nowrap cursor-default "
        + "text-zinc-500 hover:text-zinc-800 hover:bg-zinc-800/5 dark:text-zinc-400 dark:hover:text-white dark:hover:bg-white/15";

    /// <summary>The clear button while there is nothing to clear.</summary>
    internal const string ClearHidden =
        "relative hidden shrink-0 items-center justify-center gap-2 size-8 ms-1 rounded-md text-sm font-medium whitespace-nowrap cursor-default "
        + "text-zinc-500 hover:text-zinc-800 hover:bg-zinc-800/5 dark:text-zinc-400 dark:hover:text-white dark:hover:bg-white/15";

    /// <summary>The button trigger's words: the value, or the placeholder while there is none.</summary>
    internal const string Display = "block flex-1 truncate text-start text-zinc-700 dark:text-zinc-300";

    /// <summary>The placeholder inside the button trigger.</summary>
    internal const string DisplayPlaceholder = "text-zinc-400 dark:text-zinc-400";

    /// <summary>The chevron at the end of the button trigger.</summary>
    internal const string Chevron = "shrink-0 ms-2 -me-1 text-zinc-400/75 dark:text-white/60";

    /// <summary>The popover: a panel under the trigger on a pointer, a centred sheet on a phone.</summary>
    internal const string Dialog =
        "m-auto p-3 rounded-xl border border-zinc-200 dark:border-white/10 bg-white dark:bg-zinc-700 shadow-2xs "
        + "backdrop:bg-transparent max-sm:backdrop:bg-black/10 max-sm:w-[calc(100vw-4rem)]";

    /// <summary>Flux parks the dialog's initial focus on a node that is not there once it is open.</summary>
    internal const string FocusPlaceholder = "[[open]>&]:hidden";

    /// <summary>A box only the stylesheet sizes, which tells the page whether the popover is the sheet: two wide below <c>sm</c>, one above.</summary>
    internal const string SheetProbe = "absolute top-0 start-0 h-px w-px max-sm:w-0.5 invisible pointer-events-none";

    /// <summary>The parts, one under the other.</summary>
    internal const string Layout = "flex flex-col gap-3";

    /// <summary>The saturation and brightness area: three to two, never scrolled by the finger on it.</summary>
    internal const string Area = "relative w-full aspect-[3/2] rounded-lg touch-none";

    /// <summary>One layer of the area: the hue, then white from the left, then black from below.</summary>
    internal const string AreaLayer = "absolute inset-0 rounded-lg overflow-hidden";

    /// <summary>White on the left, fading to nothing.</summary>
    internal const string AreaWhite = AreaLayer + " bg-linear-to-r from-white to-transparent";

    /// <summary>Black at the bottom, fading to nothing.</summary>
    internal const string AreaBlack = AreaLayer + " bg-linear-to-b from-transparent to-black";

    /// <summary>The area's thumb, centred on the point it stands for.</summary>
    internal const string AreaThumb =
        "absolute size-4 max-sm:size-5 -translate-x-1/2 rtl:translate-x-1/2 -translate-y-1/2 rounded-full border-2 border-white "
        + "outline-offset-2 shadow-[0_0_0_1px_rgba(0,0,0,0.15)]";

    /// <summary>A slider's root.</summary>
    internal const string Slider = "flex flex-col justify-center w-full min-h-4 touch-none [--ui-slider-thumb:1rem] max-sm:[--ui-slider-thumb:1.25rem]";

    /// <summary>The row the track sits in.</summary>
    internal const string SliderRow = "flex flex-col justify-center h-full";

    /// <summary>The track.</summary>
    internal const string SliderTrack = "relative shrink-0 h-3 max-sm:h-4 rounded-full";

    /// <summary>What the track is painted with: the hues, or the colour fading in over a checkerboard.</summary>
    internal const string SliderPaint = "absolute inset-0 rounded-full";

    /// <summary>The clip the indicator sits in.</summary>
    internal const string SliderFill = "relative size-full rounded-full overflow-hidden";

    /// <summary>The indicator: the travelled part, which this slider leaves unpainted.</summary>
    internal const string SliderIndicator = "absolute inset-y-0 opacity-0";

    /// <summary>A slider's thumb.</summary>
    internal const string SliderThumb =
        "absolute top-1/2 size-4 max-sm:size-5 -translate-x-1/2 rtl:translate-x-1/2 -translate-y-1/2 rounded-full bg-white "
        + "ring-1 ring-black/15 dark:ring-black/30 shadow-[0_1px_2px_0_rgba(0,0,0,0.05),0_2px_4px_0_rgba(0,0,0,0.1)]";

    /// <summary>The row holding the popover's text input and the dropper.</summary>
    internal const string InputRow = "flex items-center gap-2";

    /// <summary>The popover's text input.</summary>
    internal const string PopoverInput =
        "block w-full appearance-none h-10 sm:h-8 px-2.5 text-base sm:text-sm rounded-lg shadow-xs "
        + "border bg-white dark:bg-white/10 text-zinc-700 dark:text-zinc-300 placeholder:text-zinc-400 "
        + "border-zinc-200 border-b-zinc-300/80 dark:border-white/10 dark:border-b-white/10";

    /// <summary>The popover's text input beside the dropper: it takes what the row has left.</summary>
    internal const string PopoverInputInRow = "flex-1";

    /// <summary>The dropper button.</summary>
    internal const string Dropper =
        "flex items-center justify-center size-10 sm:size-8 rounded-lg text-zinc-400 hover:text-zinc-800 hover:bg-zinc-100 dark:hover:text-white dark:hover:bg-white/5 "
        + "disabled:opacity-50 disabled:pointer-events-none";

    /// <summary>The palette: eight to a row.</summary>
    internal const string Swatches = "grid grid-cols-8 gap-1.5";

    /// <summary>One colour of the palette.</summary>
    internal const string Swatch =
        "size-6 max-sm:size-auto max-sm:w-full max-sm:aspect-square rounded border border-black/10 dark:border-white/10 cursor-default "
        + "data-selected:ring-2 data-selected:ring-offset-1 data-selected:ring-zinc-800 dark:data-selected:ring-white data-selected:ring-offset-white";

    /// <summary>Cancel and Select, which only the phone's sheet shows.</summary>
    internal const string Footer = "hidden max-sm:flex justify-end gap-2 pt-1";

    /// <summary>The sheet's buttons under a picker that is off, which never opens: dimmed and flat, as Flux draws them.</summary>
    internal const string FooterOff = "opacity-50 shadow-none";

    /// <summary>What assistive tech is told, out of sight.</summary>
    internal const string Announce = "sr-only";

    /// <summary>The input trigger's height, text and padding.</summary>
    internal static string TriggerSize(Ui.ColorPickerSize size) => size switch
    {
        Ui.ColorPickerSize.Sm => "h-8 py-1.5 ps-[5px] pe-2 text-sm leading-[1.125rem] rounded-md",
        Ui.ColorPickerSize.Xs => "h-6 py-1.5 ps-[3px] pe-1.5 text-xs leading-[1.125rem] rounded-md",
        _ => "h-10 py-2 ps-[7px] pe-3 text-base sm:text-sm leading-[1.375rem] rounded-lg",
    };

    /// <summary>The input trigger's end padding once a button stands there.</summary>
    internal const string TriggerWithAction = "pe-[3px]";

    /// <summary>The button trigger's height, text and padding.</summary>
    internal static string ButtonSize(Ui.ColorPickerSize size) => size switch
    {
        Ui.ColorPickerSize.Sm => "h-8 py-1.5 ps-[5px] pe-2 text-sm rounded-md",
        Ui.ColorPickerSize.Xs => "h-6 py-1.5 ps-[3px] pe-1.5 text-xs rounded-md",
        _ => "h-10 py-2 ps-[7px] pe-3 text-base sm:text-sm rounded-lg",
    };

    /// <summary>The swatch's side, by the trigger's height.</summary>
    internal static string SwatchSize(Ui.ColorPickerSize size) => size switch
    {
        Ui.ColorPickerSize.Sm => "size-5",
        Ui.ColorPickerSize.Xs => "size-[17px]",
        _ => "size-6",
    };
}
