namespace Rask;

/// <summary>The calendar's class literals, written from Flux UI's measured looks.</summary>
internal static class UiCalendarLook
{
    internal const string Root = "relative";

    internal const string HeaderHost = "relative";

    internal const string HeaderBar = "absolute z-10 top-0 inset-x-0 p-2";

    internal const string Header = "flex items-center justify-between";

    internal const string HeaderStart = "flex items-center gap-2";

    internal const string HeaderEnd = "flex items-center";

    internal const string Step =
        "flex items-center justify-center size-8 rounded-lg text-start cursor-auto text-zinc-400 "
        + "hover:bg-zinc-100 hover:text-zinc-800 dark:hover:bg-white/5 dark:hover:text-white "
        + "disabled:opacity-50 disabled:pointer-events-none";

    internal const string StepIcon = "shrink! rtl:hidden";

    internal const string StepIconRtl = "shrink! hidden rtl:inline";

    internal const string TodayBox = "relative";

    // Flux draws its own blank calendar here, filled through the path; Heroicons' mini calendar is the nearest.
    internal const string TodayIcon = "shrink! fill-none [&>path]:fill-current";

    internal const string TodayNumber =
        "absolute inset-0 mt-[3px] flex items-center justify-center text-[0.5625rem] font-semibold cursor-default";

    internal const string SelectHost = "text-sm font-medium text-zinc-800 dark:text-white";

    internal const string Select =
        "appearance-none h-8 ps-2 pe-[1.35rem] rounded-lg bg-zinc-100 dark:bg-white/10 dark:[&>option]:bg-zinc-700";

    internal const string Months = "relative flex justify-center gap-4 p-2";

    internal const string Heading = "flex items-center h-8 px-2 mb-2";

    internal const string HeadingHidden = "flex items-center h-8 px-2 mb-2 opacity-0";

    internal const string HeadingText = "text-sm font-medium text-zinc-800 dark:text-white";

    internal const string Row = "flex";

    internal const string RowBelow = "flex mt-1";

    internal const string Weekday = "flex items-center text-sm font-medium text-zinc-500 dark:text-zinc-300";

    internal const string WeekdayText = "w-full text-center";

    internal const string WeekNumber = "flex items-center justify-center text-xs font-medium text-zinc-400";

    internal const string TooltipHost = "inline-flex";

    internal const string Day =
        "flex flex-col items-center justify-center rounded-lg text-sm font-medium cursor-default "
        + "text-zinc-800 dark:text-white hover:bg-zinc-800/5 dark:hover:bg-white/5 "
        + "disabled:text-zinc-400 dark:disabled:text-zinc-400 disabled:hover:bg-transparent";

    internal const string DaySelected =
        "flex flex-col items-center justify-center rounded-lg text-sm font-medium cursor-default "
        + "bg-fx-accent text-fx-accent-foreground";

    internal const string DayStatic = "relative flex items-center justify-center rounded-lg text-sm font-medium text-zinc-800 dark:text-white";

    internal const string DayStaticSelected =
        "relative flex items-center justify-center rounded-lg text-sm font-medium bg-fx-accent text-fx-accent-foreground";

    internal const string DayText = "relative";

    internal const string TodayDot = "absolute inset-x-0 -bottom-[3px] flex items-end justify-center";

    internal const string TodayDotHidden = "absolute inset-x-0 -bottom-[3px] hidden items-end justify-center";

    internal const string StaticDot = "absolute inset-0 flex items-end justify-center";

    internal const string StaticDotHidden = "absolute inset-0 hidden items-end justify-center";

    internal const string Dot = "size-1 rounded-full bg-zinc-800 dark:bg-white";

    internal const string DotSelected = "size-1 rounded-full bg-fx-accent-foreground";

    internal static string Cell(Ui.CalendarSize size) => size switch
    {
        Ui.CalendarSize.Xs => "size-9",
        Ui.CalendarSize.Sm => "size-10",
        Ui.CalendarSize.Lg => "size-12",
        Ui.CalendarSize.Xl => "size-14",
        Ui.CalendarSize.Xxl => "size-16",
        _ => "size-11",
    };

    /// <summary>A day's cell: rounded where a row or a range starts and ends, tinted inside a range.</summary>
    internal static string Td(bool first, bool last, bool outside, bool unavailable, UiCalendarMarks marks) =>
        UiClass.Compose(
            "p-0",
            first || marks.Start ? "rounded-s-lg" : null,
            last || marks.End || marks.EndPreview ? "rounded-e-lg" : null,
            marks.InRange ? "bg-zinc-100 dark:bg-white/10" : null,
            outside ? "opacity-50" : null,
            unavailable ? "line-through" : null);
}
