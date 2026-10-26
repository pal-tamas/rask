namespace Rask;

/// <summary>The datetime picker's class literals, written from Flux UI's measured looks.</summary>
/// <remarks>The trigger, the popup and the calendar in it are the date picker's (<see cref="UiDatePickerLook" />).</remarks>
internal static class UiDatetimePickerLook
{
    // As wide as its button says, up to 288px — 320px for a range; a trigger of the page's own is its own width.
    internal const string Root = "max-w-64 sm:max-w-72";

    internal const string RootRange = "max-w-64 sm:max-w-80";

    // The calendar beside the times from `sm` up; one above the other on a phone.
    internal const string Body = "flex max-sm:flex-col";

    internal const string Calendar = "relative";

    // The column of times beside the calendar, which a phone does not draw: there the time is a field under it.
    internal const string Column = "max-sm:hidden flex flex-col w-36 border-s border-zinc-200 dark:border-white/10";

    internal const string ColumnHeading = "flex items-center gap-2 h-12 px-3 shrink-0 border-b border-zinc-200 dark:border-white/10";

    internal const string ColumnIcon = "shrink-0 text-zinc-400 dark:text-white/60";

    internal const string ColumnTitle = "text-sm font-medium text-zinc-800 dark:text-white";

    internal const string ColumnScroll = "relative flex-1";

    internal const string Times = "absolute inset-0 flex flex-col gap-px p-1.5 overflow-auto";

    // A time in the column: lit under the pointer, tinted while chosen, grey and untouchable where it cannot be picked.
    internal const string Time =
        "flex items-center gap-2 h-8 ps-2 pe-3 shrink-0 rounded-md text-sm tabular-nums cursor-default "
        + "text-zinc-800 dark:text-white hover:bg-zinc-800/5 dark:hover:bg-white/5 "
        + "data-selected:bg-zinc-100 dark:data-selected:bg-white/10 "
        + "disabled:text-zinc-400 dark:disabled:text-zinc-500 disabled:pointer-events-none";

    // Opened, the column shows the row it starts on two rows down from its top, as Flux scrolls it: on the first
    // paint with no script, the row two above it brought to the top; from the runtime at every opening after it.
    // (Flux's rows are 33px apart and its scroll is two 32px rows: the row two above sits 2px into the top.)
    internal const string TimeScrolledTo = Time + " snap-start -scroll-mt-0.5 [scroll-initial-target:nearest]";

    internal const string TimeCheck = "flex items-center shrink-0 w-4";

    internal const string TimeCheckIcon = "hidden [[data-selected]>*>&]:block";

    internal const string TimeText = "whitespace-nowrap";

    // The time under the calendar: the phone's in place of the column, and the only one with typed times.
    internal const string Row = "flex items-center gap-3 px-4 py-3 border-t border-zinc-200 dark:border-white/10";

    internal const string RowPhone = "hidden max-sm:flex items-center gap-3 px-4 py-3 border-t border-zinc-200 dark:border-white/10";

    // A range's start and end side by side, one under the other on a phone.
    internal const string Ends = "grid sm:grid-cols-2 border-t border-zinc-200 dark:border-white/10";

    internal const string End = "flex items-center gap-3 px-4 py-3";

    internal const string EndBelow = "flex items-center gap-3 px-4 py-3 max-sm:border-t border-zinc-200 dark:border-white/10";

    internal const string RowLabel = "min-w-9 shrink-0 text-sm font-medium whitespace-nowrap text-zinc-800 dark:text-white";

    internal const string Picker = "flex-1 min-w-0";

    internal const string Footer = "flex items-center justify-end gap-2 p-2 ps-4 max-sm:p-4 border-t border-zinc-200 dark:border-white/10";

    // A picker that confirms nothing on a desktop still does on a phone: Flux's sheet writes nothing until Select.
    internal const string FooterPhone = "hidden max-sm:flex items-center justify-end gap-2 p-2 ps-4 max-sm:p-4 border-t border-zinc-200 dark:border-white/10";

    internal const string Error = "sm:flex-1 text-sm font-medium text-red-500 dark:text-red-400";

    // The word between the date and the time on the button ("at", "à"), in the placeholder's grey.
    internal const string At = "text-zinc-400";

    // What the button says, cut off with an ellipsis on a narrow screen.
    internal const string Words = "truncate";

    internal const string Placeholder = "overflow-hidden text-zinc-400";

    // A typed time's hour and minute, in the text's own face here (the time picker's are monospace).
    internal const string Segment =
        "block w-[calc(2ch+2px)] p-0 rounded-sm bg-transparent text-center focus:ring-0 focus:ring-offset-0 "
        + "placeholder:text-zinc-400 disabled:text-zinc-500";

    internal const string SegmentMeridiem =
        "block w-[calc(1.5em+3px)] p-0 tabular-nums rounded-sm bg-transparent text-center focus:ring-0 focus:ring-offset-0 "
        + "placeholder:text-zinc-400 disabled:text-zinc-500";

    internal const string TimeWords = "overflow-hidden";

    /// <summary>The trigger's height — <c>xs</c> and <c>sm</c> shrink it, as Flux's do.</summary>
    internal static string Height(Ui.DatetimePickerSize? size) => size switch
    {
        Ui.DatetimePickerSize.Sm => "h-8",
        Ui.DatetimePickerSize.Xs => "h-6",
        _ => "h-10",
    };

    /// <summary>The calendar's day cells: <c>sm</c> shrinks them, <c>lg</c> and up enlarge them.</summary>
    internal static Ui.CalendarSize Cells(Ui.DatetimePickerSize? size) => size switch
    {
        Ui.DatetimePickerSize.Sm => Ui.CalendarSize.Xs,
        Ui.DatetimePickerSize.Lg => Ui.CalendarSize.Base,
        Ui.DatetimePickerSize.Xl => Ui.CalendarSize.Lg,
        Ui.DatetimePickerSize.Xxl => Ui.CalendarSize.Xl,
        _ => Ui.CalendarSize.Sm,
    };
}
