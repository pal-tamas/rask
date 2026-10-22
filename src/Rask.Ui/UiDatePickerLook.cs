namespace Rask;

/// <summary>The date picker's class literals, written from Flux UI's measured looks.</summary>
internal static class UiDatePickerLook
{
    internal const string Root = "min-w-[12rem] sm:min-w-[240px]";

    // The field-shaped box both triggers are drawn as.
    private const string Box =
        "flex items-center w-full px-3 py-2 rounded-lg overflow-hidden cursor-default "
        + "border border-zinc-200 border-b-zinc-300/80 bg-white shadow-xs "
        + "dark:border-white/10 dark:border-b-white/10 dark:bg-white/10 "
        + "data-invalid:border-red-500 dark:data-invalid:border-red-500 data-invalid:shadow-none";

    internal const string Button = Box + " text-base sm:text-sm disabled:shadow-none disabled:border-b-zinc-200 dark:disabled:bg-white/[7%]";

    internal const string Typed = Box + " text-base sm:text-sm leading-[1.375rem] text-zinc-700 dark:text-zinc-300 dark:shadow-none";

    internal const string Leading = "me-2 text-zinc-400/75 dark:text-white/60";

    internal const string Trailing = "ms-2 -me-1 text-zinc-400/75 dark:text-white/60";

    internal const string Selected = "flex flex-1 gap-2 overflow-hidden whitespace-nowrap text-start text-zinc-700 dark:text-zinc-300";

    internal const string Placeholder = "text-zinc-400";

    internal const string Segments = "flex items-center -ms-px min-w-0 overflow-hidden";

    // Two characters wide, or four for the year, in the app's monospace face, plus a pixel either side.
    internal const string Segment = "w-[calc(2ch+2px)] p-0 rounded-sm font-mono tabular-nums text-center cursor-text focus:ring-0 focus:ring-offset-0";

    internal const string SegmentYear = "w-[calc(4ch+2px)] p-0 rounded-sm font-mono tabular-nums text-center cursor-text focus:ring-0 focus:ring-offset-0";

    internal const string Spacer = "flex-1";

    // The browser's date field in the input's box: no box of its own, and its own picker button hidden — the
    // field's calendar is the picker's.
    internal const string Native =
        "min-w-0 flex-1 p-0 border-0 bg-transparent shadow-none focus:ring-0 focus:outline-hidden tabular-nums "
        + "[&::-webkit-calendar-picker-indicator]:hidden";

    // From `sm` up the popup hangs under the trigger's start, five pixels away: CSS anchor positioning against the
    // picker, whose name the dialog's own style states, and the runtime keeps it inside the viewport from there
    // (data-rask-place). Under `sm` it is Flux's sheet — centred over a dimmed page, where the browser puts a
    // popover that is told nothing — and the runtime leaves it there (--rask-place).
    // The popup takes the focus as it opens (autofocus); Flux's draws no ring however it was opened.
    internal const string Dialog =
        "focus:outline-hidden p-0 rounded-xl border border-zinc-200 dark:border-white/10 bg-white dark:bg-zinc-700 shadow-2xs text-inherit "
        + "[&:popover-open]:max-w-[calc(100%-2em-6px)] overflow-visible [&:popover-open]:overflow-auto "
        + "sm:inset-auto sm:top-[anchor(bottom)] sm:left-[anchor(left)] sm:mx-0 sm:my-[5px] "
        + "sm:[position-try-fallbacks:flip-block,flip-inline] "
        + "max-sm:m-auto max-sm:max-h-full max-sm:border-0 max-sm:shadow-xl max-sm:backdrop:bg-black/10 "
        + "max-sm:[--rask-place:none]";

    // Flux parks the dialog's initial focus on a node that is not there once it is open.
    internal const string FocusPlaceholder = "[:popover-open>&]:hidden";

    internal const string Calendar = "relative grid sm:grid-cols-[auto_1fr]";

    internal const string Aside = "sm:row-span-3";

    // The list beside the months from `sm` up; under it the same presets are the select below the months.
    internal const string Presets = "max-sm:hidden row-span-3 border-e border-zinc-200 dark:border-zinc-600";

    internal const string PresetSelect = "sm:hidden px-4";

    // The browser's own select in the input's box, as Flux draws this one: 14px at every width, a flat shadow.
    internal const string PresetSelectControl =
        UiInputLook.Control + " h-10 py-2 ps-3 pe-10 text-sm rounded-lg shadow-2xs " + UiInputLook.Outline;

    internal const string PresetList = "flex flex-col gap-1 p-2 min-w-[120px]";

    internal const string Preset =
        "block w-full px-2 py-1.5 rounded-lg text-sm font-medium text-start whitespace-nowrap cursor-auto "
        + "text-zinc-600 dark:text-zinc-300 hover:bg-zinc-100 hover:text-zinc-800 dark:hover:bg-white/5 dark:hover:text-white";

    internal const string PresetChecked =
        "block w-full px-2 py-1.5 rounded-lg text-sm font-medium text-start whitespace-nowrap cursor-auto "
        + "bg-accent text-accent-foreground dark:hover:bg-white/5 dark:hover:text-white";

    internal const string Footer = "sm:col-start-2 flex justify-end gap-2 p-2 max-sm:p-4";

    // A picker that does not ask for confirmation still does on a phone: Flux's sheet writes nothing until its
    // button is pressed.
    internal const string FooterPhone = "sm:col-start-2 hidden max-sm:flex justify-end gap-2 p-2 max-sm:p-4";

    internal const string FooterButton = "inline max-sm:block";

    internal static string Height(int rank) => rank switch
    {
        1 => "h-8",
        2 => "h-6",
        _ => "h-10",
    };

    internal static Ui.CalendarSize Cells(Ui.DatePickerSize? size) => size switch
    {
        Ui.DatePickerSize.Sm => Ui.CalendarSize.Xs,
        Ui.DatePickerSize.Lg => Ui.CalendarSize.Base,
        Ui.DatePickerSize.Xl => Ui.CalendarSize.Lg,
        Ui.DatePickerSize.Xxl => Ui.CalendarSize.Xl,
        _ => Ui.CalendarSize.Sm,
    };
}
