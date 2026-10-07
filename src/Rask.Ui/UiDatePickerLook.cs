namespace Rask;

/// <summary>The date picker's class literals, written from Flux UI's measured looks.</summary>
internal static class UiDatePickerLook
{
    internal const string Root = "min-w-[240px]";

    // The field-shaped box both triggers are drawn as.
    private const string Box =
        "flex items-center w-full px-3 py-2 rounded-lg overflow-hidden cursor-default "
        + "border border-zinc-200 border-b-zinc-300/80 bg-white shadow-xs "
        + "dark:border-white/10 dark:border-b-white/10 dark:bg-white/10 "
        + "data-invalid:border-red-500 dark:data-invalid:border-red-500 data-invalid:shadow-none";

    internal const string Button = Box + " text-sm disabled:shadow-none disabled:border-b-zinc-200 dark:disabled:bg-white/[7%]";

    internal const string Typed = Box + " text-sm leading-[1.375rem] text-zinc-700 dark:text-zinc-300 dark:shadow-none";

    internal const string Leading = "me-2 text-zinc-400/75 dark:text-white/60";

    internal const string Trailing = "ms-2 -me-1 text-zinc-400/75 dark:text-white/60";

    internal const string Selected = "flex flex-1 gap-2 overflow-hidden whitespace-nowrap text-start text-zinc-700 dark:text-zinc-300";

    internal const string Placeholder = "text-zinc-400";

    internal const string Segments = "flex items-center -ms-px min-w-0 overflow-hidden";

    // Two characters wide, or four for the year, in the app's monospace face, plus a pixel either side.
    internal const string Segment = "w-[calc(2ch+2px)] p-0 rounded-sm font-mono tabular-nums text-center cursor-text focus:ring-0 focus:ring-offset-0";

    internal const string SegmentYear = "w-[calc(4ch+2px)] p-0 rounded-sm font-mono tabular-nums text-center cursor-text focus:ring-0 focus:ring-offset-0";

    internal const string Spacer = "flex-1";

    internal const string Dialog =
        "p-0 rounded-xl border border-zinc-200 dark:border-white/10 bg-white dark:bg-zinc-700 shadow-2xs text-inherit "
        + "[&:popover-open]:max-w-[calc(100%-2em-6px)] overflow-visible [&:popover-open]:overflow-auto";

    // Flux parks the dialog's initial focus on a node that is not there once it is open.
    internal const string FocusPlaceholder = "[:popover-open>&]:hidden";

    internal const string Calendar = "relative grid grid-cols-[auto_1fr]";

    internal const string Aside = "row-span-3";

    internal const string Presets = "row-span-3 border-e border-zinc-200 dark:border-zinc-600";

    internal const string PresetList = "flex flex-col gap-1 p-2 min-w-[120px]";

    internal const string Preset =
        "block w-full px-2 py-1.5 rounded-lg text-sm font-medium text-start whitespace-nowrap cursor-auto "
        + "text-zinc-600 dark:text-zinc-300 hover:bg-zinc-100 hover:text-zinc-800 dark:hover:bg-white/5 dark:hover:text-white";

    internal const string PresetChecked =
        "block w-full px-2 py-1.5 rounded-lg text-sm font-medium text-start whitespace-nowrap cursor-auto "
        + "bg-fx-accent text-fx-accent-foreground dark:hover:bg-white/5 dark:hover:text-white";

    internal const string Footer = "col-start-2 flex justify-end gap-2 p-2";

    internal const string FooterHidden = "col-start-2 hidden justify-end gap-2 p-2";

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
